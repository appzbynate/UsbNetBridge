using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UsbNetBridge.Client;

/// <summary>
/// Listens for push events from the Android phone (UDP 3242).
/// Gone: UNB1-gone|phoneIp|busid:vid:pid
/// Offline: UNB1-off|ip[,ip…]
/// </summary>
public sealed class PhoneEventListener : IDisposable
{
    public const int EventPort = 3242;
    public const string GonePrefix = "UNB1-gone|";
    public const string OfflinePrefix = "UNB1-off|";

    public sealed record DeviceGoneEvent(string PhoneHost, string BusId, string? Vid, string? Pid);
    public sealed record ServerOfflineEvent(IReadOnlyList<string> Hosts);

    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _udp;
    private Task? _loop;
    private bool _disposed;

    public event Action<DeviceGoneEvent>? DeviceGone;
    public event Action<ServerOfflineEvent>? ServerOffline;
    public event Action<string>? Log;

    public void Start()
    {
        if (_loop != null) return;
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            LanDiscovery.SuppressIcmpUnreachable(_udp);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, EventPort));
            _udp.EnableBroadcast = true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Could not listen for phone events on UDP {EventPort}: {ex.Message}");
            return;
        }

        _loop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        Log?.Invoke($"Listening for phone events on UDP {EventPort}");
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var udp = _udp;
        if (udp == null) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                var text = Encoding.UTF8.GetString(result.Buffer).Trim();
                var packetHost = result.RemoteEndPoint.Address.ToString();

                if (TryParseOffline(text, out var offline))
                {
                    var hosts = MergeHosts(offline.Hosts, packetHost);
                    Log?.Invoke("USB host reported server stopped" +
                                (hosts.Count > 0 ? $" ({string.Join(", ", hosts)})" : ""));
                    ServerOffline?.Invoke(new ServerOfflineEvent(hosts));
                    continue;
                }

                if (!TryParseGone(text, out var evt))
                    continue;

                // Prefer advertised phone IP; fall back to packet source.
                var host = string.IsNullOrWhiteSpace(evt.PhoneHost) || evt.PhoneHost == "0.0.0.0"
                    ? packetHost
                    : evt.PhoneHost;
                var gone = evt with { PhoneHost = host };
                Log?.Invoke($"USB host reported unplug: [{gone.BusId}]" +
                            (gone.Vid != null && gone.Pid != null ? $" {gone.Vid}:{gone.Pid}" : "") +
                            $" from {gone.PhoneHost}");
                DeviceGone?.Invoke(gone);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    Log?.Invoke("Phone event listener: " + ex.Message);
                try { await Task.Delay(500, ct).ConfigureAwait(false); } catch { /* ignore */ }
            }
        }
    }

    public static bool TryParseGone(string text, out DeviceGoneEvent evt)
    {
        evt = null!;
        if (!text.StartsWith(GonePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var body = text[GonePrefix.Length..];
        var parts = body.Split('|');
        if (parts.Length < 2)
            return false;

        var phoneHost = parts[0].Trim();
        var devicePart = parts[1].Trim();
        var bits = devicePart.Split(':');
        if (bits.Length < 1 || string.IsNullOrWhiteSpace(bits[0]))
            return false;

        var busId = bits[0].Trim();
        string? vid = bits.Length >= 2 ? bits[1].Trim() : null;
        string? pid = bits.Length >= 3 ? bits[2].Trim() : null;
        evt = new DeviceGoneEvent(phoneHost, busId, vid, pid);
        return true;
    }

    public static bool TryParseOffline(string text, out ServerOfflineEvent evt)
    {
        evt = null!;
        if (!text.StartsWith(OfflinePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var body = text[OfflinePrefix.Length..].Trim();
        var hosts = body.Split('|', ',')
            .Select(h => h.Trim())
            .Where(h => h.Length > 0 && h != "0.0.0.0")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        evt = new ServerOfflineEvent(hosts);
        return true;
    }

    private static IReadOnlyList<string> MergeHosts(IReadOnlyList<string> advertised, string packetHost)
    {
        var hosts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? host)
        {
            if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0") return;
            if (seen.Add(host.Trim()))
                hosts.Add(host.Trim());
        }
        foreach (var h in advertised)
            Add(h);
        Add(packetHost);
        return hosts;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { _udp?.Dispose(); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
