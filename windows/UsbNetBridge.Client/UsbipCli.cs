using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace UsbNetBridge.Client;

/// <summary>
/// Thin wrapper around usbip-win2's <c>usbip.exe</c> (list / attach / detach).
/// After attach, Windows loads real device drivers (e.g. HID mouse).
/// </summary>
public sealed class UsbipCli
{
    // usbip list -r output looks like:
    //   Exportable USB devices
    //   ======================
    //    - 192.168.1.10
    //           1-2: Vendor : Product (1234:5678)
    // The busid is on the indented line, NOT the "-" host line.
    private static readonly Regex DeviceBlock = new(
        @"^\s+(?<busid>\d+(?:[.-]\d+)+)\s*:\s*(?<desc>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex VidPid = new(
        @"\((?<vid>[0-9a-fA-F]{4}):(?<pid>[0-9a-fA-F]{4})\)",
        RegexOptions.Compiled);

    private static readonly Regex PortLine = new(
        @"Port\s+(?<port>\d+).*?Busid\s+(?<busid>[0-9\-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex AttachedPortBlock = new(
        @"Port\s+(?<port>\d+)\s*:\s*(?<headline>[^\r\n]*)(?<body>.*?)(?=Port\s+\d+\s*:|\z)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex UsbIpUrl = new(
        @"usbip://(?<host>[^:/]+)(?::(?<tcp>\d+))?/(?<busid>\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string ExePath { get; }

    public UsbipCli(string? exePath = null)
    {
        ExePath = exePath ?? FindUsbipExe()
            ?? throw new FileNotFoundException(
                "usbip.exe not found. Install usbip-win2 from https://github.com/vadimgrn/usbip-win2/releases " +
                "and restart this app.");
    }

    public static string? FindUsbipExe()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "USBip", "usbip.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "USBip", "usbip.exe"),
            Path.Combine(AppContext.BaseDirectory, "usbip.exe"),
            "usbip.exe",
        };
        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(c)) return Path.GetFullPath(c);
                // Also try PATH resolution for bare name
                if (c == "usbip.exe")
                {
                    var fromPath = FindOnPath("usbip.exe");
                    if (fromPath != null) return fromPath;
                }
            }
            catch
            {
                // ignore
            }
        }
        return null;
    }

    public static bool IsInstalled() => FindUsbipExe() != null;

    public async Task<IReadOnlyList<RemoteUsbDevice>> ListRemoteAsync(string host, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!ct.CanBeCanceled)
            cts.CancelAfter(TimeSpan.FromSeconds(2));
        var (code, stdout, stderr) = await RunAsync(new[] { "list", "-r", host }, cts.Token).ConfigureAwait(false);
        var text = stdout + "\n" + stderr;
        if (code != 0 && !text.Contains("Exportable USB devices", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(CleanError(text, code));
        }

        var list = new List<RemoteUsbDevice>();
        foreach (Match m in DeviceBlock.Matches(text))
        {
            var busid = m.Groups["busid"].Value.Trim();
            var desc = m.Groups["desc"].Value.Trim();
            // Skip accidental host lines if any match
            if (busid.Count(c => c == '.') >= 2) continue;
            string? vid = null, pid = null;
            var vp = VidPid.Match(desc);
            if (vp.Success)
            {
                vid = vp.Groups["vid"].Value;
                pid = vp.Groups["pid"].Value;
            }
            list.Add(new RemoteUsbDevice(busid, desc, vid, pid));
        }

        if (list.Count == 0 && code != 0)
        {
            throw new InvalidOperationException(CleanError(text, code));
        }

        return list;
    }

    public async Task AttachAsync(string host, string busId, CancellationToken ct = default)
    {
        var (code, stdout, stderr) = await RunAsync(
            new[] { "attach", "-r", host, "-b", busId }, ct).ConfigureAwait(false);
        var text = stdout + "\n" + stderr;
        if (code != 0 && !text.Contains("successfully attached", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(CleanError(text, code));
    }

    public async Task DetachAsync(int port, CancellationToken ct = default)
    {
        var (code, stdout, stderr) = await RunAsync(
            new[] { "detach", "-p", port.ToString() }, ct).ConfigureAwait(false);
        var text = stdout + "\n" + stderr;
        if (code != 0 && !text.Contains("successfully detached", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(CleanError(text, code));
    }

    public async Task DetachAllAsync(CancellationToken ct = default)
    {
        // usbip-win2 supports detach -a / -all depending on version; try common forms.
        var (code, stdout, stderr) = await RunAsync(new[] { "detach", "-a" }, ct).ConfigureAwait(false);
        var text = stdout + "\n" + stderr;
        if (code == 0 || text.Contains("successfully", StringComparison.OrdinalIgnoreCase))
            return;

        (code, stdout, stderr) = await RunAsync(new[] { "detach", "--all" }, ct).ConfigureAwait(false);
        text = stdout + "\n" + stderr;
        if (code == 0 || text.Contains("successfully", StringComparison.OrdinalIgnoreCase))
            return;

        // Fallback: parse port state if available
        var (pc, pout, perr) = await RunAsync(new[] { "port" }, ct).ConfigureAwait(false);
        var ports = pout + "\n" + perr;
        foreach (Match m in Regex.Matches(ports, @"Port\s+(\d+)", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(m.Groups[1].Value, out var p))
            {
                try { await DetachAsync(p, ct).ConfigureAwait(false); } catch { /* continue */ }
            }
        }
    }

    public async Task<string> PortStatusAsync(CancellationToken ct = default)
    {
        var (_, stdout, stderr) = await RunAsync(new[] { "port" }, ct).ConfigureAwait(false);
        return (stdout + "\n" + stderr).Trim();
    }

    public async Task<IReadOnlyList<AttachedUsbDevice>> ListAttachedAsync(CancellationToken ct = default)
    {
        var raw = await PortStatusAsync(ct).ConfigureAwait(false);
        return ParseAttachedDevices(raw);
    }

    public static IReadOnlyList<AttachedUsbDevice> ParseAttachedDevices(string raw)
    {
        var list = new List<AttachedUsbDevice>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;

        foreach (Match m in AttachedPortBlock.Matches(raw))
        {
            var headline = m.Groups["headline"].Value.Trim();
            if (headline.Contains("not in use", StringComparison.OrdinalIgnoreCase) ||
                headline.Contains("empty", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!headline.Contains("in use", StringComparison.OrdinalIgnoreCase) &&
                !headline.Contains("attached", StringComparison.OrdinalIgnoreCase) &&
                !headline.Contains("imported", StringComparison.OrdinalIgnoreCase) &&
                !m.Groups["body"].Value.Contains("usbip://", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!int.TryParse(m.Groups["port"].Value, out var port))
                continue;

            var body = m.Groups["body"].Value;
            string? vid = null, pid = null, desc = null, host = null, busId = null, speed = null;

            var speedMatch = Regex.Match(headline, @"at\s+(?<speed>[^\r\n]+)", RegexOptions.IgnoreCase);
            if (speedMatch.Success)
                speed = speedMatch.Groups["speed"].Value.Trim();

            foreach (var line in body.Split('\n'))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t) || t.StartsWith("->", StringComparison.Ordinal))
                    continue;
                var vp = VidPid.Match(t);
                if (vp.Success)
                {
                    vid = vp.Groups["vid"].Value;
                    pid = vp.Groups["pid"].Value;
                    desc = t;
                    var cut = t.LastIndexOf('(');
                    if (cut > 0) desc = t[..cut].Trim().TrimEnd(':').Trim();
                    break;
                }
            }

            var url = UsbIpUrl.Match(body);
            if (url.Success)
            {
                host = url.Groups["host"].Value.Trim();
                busId = url.Groups["busid"].Value.Trim();
            }

            if (string.IsNullOrWhiteSpace(desc) ||
                desc.Contains("unknown product", StringComparison.OrdinalIgnoreCase))
            {
                // Keep vendor prefix when product is "unknown product".
                var vendorCut = desc?.IndexOf(':') ?? -1;
                if (vendorCut > 0)
                    desc = desc![..vendorCut].Trim();
                if (string.IsNullOrWhiteSpace(desc) ||
                    desc.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                    desc = "USB device";
            }

            list.Add(new AttachedUsbDevice(port, desc, vid, pid, host, busId, speed));
        }

        return list;
    }

    /// <summary>True if usbip-win2 currently has at least one attached remote port.</summary>
    public async Task<bool> HasAttachedPortsAsync(CancellationToken ct = default)
    {
        var attached = await ListAttachedAsync(ct).ConfigureAwait(false);
        return attached.Count > 0;
    }

    private async Task<(int Code, string StdOut, string StdErr)> RunAsync(
        string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        if (!proc.Start())
            throw new InvalidOperationException("Failed to start usbip.exe");

        try
        {
            // Read streams to completion before observing exit — BeginOutputReadLine can
            // miss trailing lines if WaitForExit returns first (empty "Active" UI).
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return (proc.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw;
        }
    }

    private static string CleanError(string text, int code)
    {
        var t = text.Trim();
        if (string.IsNullOrEmpty(t)) return $"usbip.exe failed (exit {code})";
        return t.Length > 800 ? t[..800] + "…" : t;
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(full)) return full;
            }
            catch { /* ignore */ }
        }
        return null;
    }
}

public sealed record AttachedUsbDevice(
    int Port,
    string Description,
    string? Vid,
    string? Pid,
    string? RemoteHost,
    string? BusId,
    string? Speed,
    string? PhoneLabel = null,
    bool AutoConnect = false)
{
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(PhoneLabel))
                return PhoneLabel.Trim();
            var desc = Description.Trim();
            if (string.IsNullOrWhiteSpace(desc) || desc.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                desc = "USB device";
            return desc;
        }
    }

    public override string ToString()
    {
        var id = Vid != null && Pid != null ? $" ({Vid}:{Pid})" : "";
        var from = !string.IsNullOrEmpty(RemoteHost) ? $"  ← {RemoteHost}" : "";
        var bus = !string.IsNullOrEmpty(BusId) ? $"  [{BusId}]" : "";
        return $"{DisplayName}{id}{from}{bus}  · Port {Port:D2}";
    }
}

public sealed record RemoteUsbDevice(
    string BusId,
    string Description,
    string? Vid,
    string? Pid,
    string? PhoneLabel = null,
    bool AutoConnect = false,
    string? ClassHint = null)
{
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(PhoneLabel))
                return PhoneLabel.Trim();
            var desc = Description.Trim();
            // Strip trailing (vid:pid) if present — shown separately when useful.
            var cut = desc.LastIndexOf('(');
            if (cut > 0) desc = desc[..cut].Trim().TrimEnd(':').Trim();
            if (string.IsNullOrWhiteSpace(desc) || desc.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                desc = "USB device";
            return desc;
        }
    }

    public override string ToString()
    {
        if (Vid != null && Pid != null)
            return $"{DisplayName}   ({Vid}:{Pid})   [{BusId}]";
        return $"{DisplayName}   [{BusId}]";
    }
}
