using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace UsbNetBridge.Client;

/// <summary>
/// Finds a phone running UsbNetBridge on the LAN via UDP discovery (port 3241).
/// Probe: UNB1?   Reply: UNB1!|ip|3240|UsbNetBridge
/// </summary>
public static class LanDiscovery
{
    public const int DiscoveryPort = 3241;
    public const string Probe = "UNB1?";
    public const string ReplyPrefix = "UNB1!";

    public sealed record FoundServer(
        string Host,
        int UsbIpPort,
        string Name,
        string? FromAddress,
        IReadOnlyList<PluggedUsbDevice> PluggedDevices,
        /// <summary>True when the reply includes a <c>dev=</c> field (even if empty).</summary>
        bool ReportsPluggedDevices,
        int? BatteryPercent = null,
        bool BatteryCharging = false,
        int? WifiQuality = null,
        string? BusyHost = null,
        string? BusyPc = null);

    public sealed record PluggedUsbDevice(string BusId, string? Vid, string? Pid, string? Label, string? ClassHint = null);

    public static async Task<IReadOnlyList<FoundServer>> FindAsync(
        TimeSpan timeout,
        IProgress<string>? log = null,
        CancellationToken ct = default,
        IEnumerable<string>? hintHosts = null,
        ICollection<string>? offlineHosts = null)
    {
        var found = new Dictionary<string, FoundServer>(StringComparer.OrdinalIgnoreCase);
        var probe = Probe + "|pc=" + Uri.EscapeDataString(Environment.MachineName);
        var probeBytes = Encoding.UTF8.GetBytes(probe);

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        SuppressIcmpUnreachable(udp);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        log?.Report($"Listening for UsbNetBridge on UDP {DiscoveryPort} ({timeout.TotalSeconds:0.#}s)…");

        // Broadcasts often fail over VPN (OpenVPN rarely forwards them). Always also
        // unicast to known phone IPs and include tunnel adapters' subnet broadcasts.
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach (var b in SubnetBroadcasts(includeTunnels: true))
            targets.Add(b);
        foreach (var hint in ExpandHintTargets(hintHosts))
            targets.Add(hint);

        foreach (var target in targets)
        {
            try
            {
                await udp.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(target, DiscoveryPort))
                    .WaitAsync(ct).ConfigureAwait(false);
                log?.Report($"Probe → {target}");
            }
            catch (Exception ex)
            {
                log?.Report($"Probe to {target} failed: {ex.Message}");
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udp.ReceiveAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    // Windows maps ICMP Port Unreachable to WSAECONNRESET (10054) on UDP.
                    // A probe to a stale/saved IP must not abort Find phone.
                    continue;
                }
                var text = Encoding.UTF8.GetString(result.Buffer).Trim();
                if (PhoneEventListener.TryParseOffline(text, out var offline))
                {
                    if (offlineHosts != null)
                    {
                        foreach (var h in offline.Hosts)
                            offlineHosts.Add(h);
                        var src = result.RemoteEndPoint.Address.ToString();
                        if (!string.IsNullOrWhiteSpace(src) && src != "0.0.0.0")
                            offlineHosts.Add(src);
                    }
                    log?.Report("USB host reported server stopped.");
                    continue;
                }
                if (!TryParseReply(text, out var server))
                    continue;

                // Prefer the address the reply actually came from. The payload often
                // advertises Wi‑Fi (192.168.x) while we reached the phone over VPN (10.8.x).
                var source = result.RemoteEndPoint.Address.ToString();
                var advertised = server.Host;
                var host = string.IsNullOrWhiteSpace(source) || source == "0.0.0.0"
                    ? (string.IsNullOrWhiteSpace(advertised) || advertised == "0.0.0.0" ? source : advertised)
                    : source;

                if (!found.ContainsKey(host))
                {
                    var entry = server with { Host = host, FromAddress = result.RemoteEndPoint.Address.ToString() };
                    found[host] = entry;
                    log?.Report($"Found {entry.Name} at {entry.Host}:{entry.UsbIpPort}" +
                                (!string.IsNullOrWhiteSpace(advertised) &&
                                 !string.Equals(advertised, entry.Host, StringComparison.OrdinalIgnoreCase)
                                    ? $" (phone LAN {advertised})"
                                    : "") +
                                (entry.PluggedDevices.Count > 0
                                    ? $" ({entry.PluggedDevices.Count} USB)"
                                    : ""));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // timeout
        }

        return found.Values.ToList();
    }

    /// <summary>
    /// Round-trip to the phone's discovery port. Used when ICMP ping is blocked (VPN).
    /// Does not touch USB/IP TCP 3240.
    /// </summary>
    public static async Task<long?> MeasureRttAsync(string host, TimeSpan timeout, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return null;

        var probe = Probe + "|pc=" + Uri.EscapeDataString(Environment.MachineName);
        var probeBytes = Encoding.UTF8.GetBytes(probe);
        using var udp = new UdpClient();
        SuppressIcmpUnreachable(udp);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var sw = Stopwatch.StartNew();
        try
        {
            await udp.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(ip, DiscoveryPort))
                .WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udp.ReceiveAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    continue;
                }

                var text = Encoding.UTF8.GetString(result.Buffer).Trim();
                if (text.StartsWith(ReplyPrefix, StringComparison.Ordinal))
                    return Math.Max(1, sw.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
            // timeout
        }

        return null;
    }

    /// <summary>
    /// Windows reports ICMP Port Unreachable on UDP sockets as WSAECONNRESET.
    /// Disable that so discovery probes to offline hosts don't kill the socket.
    /// </summary>
    public static void SuppressIcmpUnreachable(UdpClient udp)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            const int sioUdpConnreset = -1744830452; // SIO_UDP_CONNRESET
            udp.Client.IOControl(sioUdpConnreset, new byte[] { 0, 0, 0, 0 }, null);
        }
        catch
        {
            // Best-effort; receive loop also ignores SocketException.
        }
    }

    private static IEnumerable<IPAddress> ExpandHintTargets(IEnumerable<string>? hintHosts)
    {
        if (hintHosts == null)
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in hintHosts)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var host = raw.Trim();
            if (!seen.Add(host))
                continue;
            if (!IPAddress.TryParse(host, out var ip) ||
                ip.AddressFamily != AddressFamily.InterNetwork ||
                IPAddress.IsLoopback(ip))
                continue;

            yield return ip;

            // Directed broadcast for that /24 (helps some VPN → LAN setups).
            var bytes = ip.GetAddressBytes();
            if (IsPrivateIpv4(bytes))
            {
                yield return new IPAddress(new byte[] { bytes[0], bytes[1], bytes[2], 255 });
            }
        }
    }

    private static bool IsPrivateIpv4(byte[] b) =>
        b.Length == 4 && (
            b[0] == 10 ||
            (b[0] == 192 && b[1] == 168) ||
            (b[0] == 172 && b[1] >= 16 && b[1] <= 31));

    private static bool TryParseReply(string text, out FoundServer server)
    {
        server = null!;
        if (!text.StartsWith(ReplyPrefix, StringComparison.Ordinal))
            return false;

        // UNB1!|ip|port|name|dev=busid:vid:pid:urlencodedLabel,...
        var body = text[ReplyPrefix.Length..];
        if (body.StartsWith('|'))
            body = body[1..];

        var parts = body.Split('|');
        if (parts.Length < 2)
            return false;

        var host = parts[0].Trim();
        if (!int.TryParse(parts[1].Trim(), out var port))
            port = 3240;
        var name = parts.Length >= 3 ? parts[2].Trim() : "UsbNetBridge";
        if (string.IsNullOrEmpty(name))
            name = "UsbNetBridge";

        var plugged = new List<PluggedUsbDevice>();
        var reportsDevices = false;
        int? battery = null;
        var charging = false;
        int? wifi = null;
        string? busyHost = null;
        string? busyPc = null;

        for (var i = 3; i < parts.Length; i++)
        {
            var field = parts[i].Trim();
            if (field.StartsWith("dev=", StringComparison.OrdinalIgnoreCase))
            {
                reportsDevices = true;
                ParseDeviceList(field[4..], plugged);
            }
            else if (field.StartsWith("bat=", StringComparison.OrdinalIgnoreCase))
            {
                var raw = field[4..].Trim();
                charging = raw.EndsWith('c') || raw.EndsWith('C');
                if (charging) raw = raw[..^1];
                if (int.TryParse(raw, out var pct))
                    battery = Math.Clamp(pct, 0, 100);
            }
            else if (field.StartsWith("wifi=", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(field[5..].Trim(), out var q))
            {
                wifi = Math.Clamp(q, 0, 100);
            }
            else if (field.StartsWith("busy=", StringComparison.OrdinalIgnoreCase))
            {
                busyHost = DecodeField(field[5..]);
            }
            else if (field.StartsWith("pc=", StringComparison.OrdinalIgnoreCase))
            {
                busyPc = DecodeField(field[3..]);
            }
            else if (field.Length > 0 && !field.Contains('='))
            {
                reportsDevices = true;
                ParseDeviceList(field, plugged);
            }
        }

        server = new FoundServer(host, port, name, null, plugged, reportsDevices,
            battery, charging, wifi, busyHost, busyPc);
        return true;
    }

    private static void ParseDeviceList(string devPart, List<PluggedUsbDevice> plugged)
    {
        foreach (var token in devPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // busid:vid:pid[:urlencodedLabel[:class]]
            var bits = token.Split(':');
            if (bits.Length < 1 || string.IsNullOrWhiteSpace(bits[0]))
                continue;
            var busId = bits[0].Trim();
            string? vid = bits.Length >= 2 ? bits[1].Trim() : null;
            string? pid = bits.Length >= 3 ? bits[2].Trim() : null;
            string? label = null;
            string? cls = null;
            if (bits.Length >= 4 && !string.IsNullOrWhiteSpace(bits[3]))
            {
                try { label = Uri.UnescapeDataString(bits[3].Trim()); }
                catch { label = bits[3].Trim(); }
            }
            if (bits.Length >= 5 && !string.IsNullOrWhiteSpace(bits[4]))
                cls = bits[4].Trim().ToLowerInvariant();
            plugged.Add(new PluggedUsbDevice(busId, vid, pid, label, cls));
        }
    }

    private static string DecodeField(string raw)
    {
        raw = raw.Trim();
        try { return Uri.UnescapeDataString(raw); }
        catch { return raw; }
    }

    private static IEnumerable<IPAddress> SubnetBroadcasts(bool includeTunnels)
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            // OpenVPN / WireGuard often appear as Tunnel — include them for VPN→LAN find.
            if (!includeTunnels && ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(ua.Address))
                    continue;

                var mask = ua.IPv4Mask;
                if (mask is null)
                    continue;

                var ipBytes = ua.Address.GetAddressBytes();
                var maskBytes = mask.GetAddressBytes();
                if (ipBytes.Length != 4 || maskBytes.Length != 4)
                    continue;

                var bcast = new byte[4];
                for (var i = 0; i < 4; i++)
                    bcast[i] = (byte)(ipBytes[i] | ~maskBytes[i]);

                yield return new IPAddress(bcast);
            }
        }
    }
}
