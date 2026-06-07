// -----------------------------------------------------------------------
// LazyCaddy - active upstream reachability probe.
//
// Runs ONLY on the background poll thread. Uses a non-blocking async TCP
// connect with a timeout; never blocks the UI thread.
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Net.Sockets;
using LazyCaddy.Configuration;
using LazyCaddy.Models;

namespace LazyCaddy.Services;

public sealed class UpstreamProber
{
    private readonly int _timeoutMs;

    public UpstreamProber(LazyCaddyConfig config) => _timeoutMs = config.ProbeTimeoutMs;

    /// <summary>
    /// Probe one upstream's "host:port" with an async TCP connect. Returns the upstream
    /// with <see cref="UpstreamReachability"/> and measured latency filled in.
    /// </summary>
    public async Task<Upstream> ProbeAsync(Upstream upstream, CancellationToken ct = default)
    {
        if (!TryParseHostPort(upstream.Address, out var host, out var port))
            return upstream.WithProbe(UpstreamReachability.Unknown, null);

        var sw = Stopwatch.StartNew();
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeoutMs);

        try
        {
            await socket.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
            sw.Stop();
            return upstream.WithProbe(UpstreamReachability.Up, sw.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out (our linked CTS fired, not the caller's).
            return upstream.WithProbe(UpstreamReachability.Down, null);
        }
        catch (SocketException)
        {
            return upstream.WithProbe(UpstreamReachability.Down, null);
        }
    }

    /// <summary>Probe every upstream concurrently.</summary>
    public async Task<IReadOnlyList<Upstream>> ProbeAllAsync(
        IReadOnlyList<Upstream> upstreams, CancellationToken ct = default)
    {
        var tasks = upstreams.Select(u => ProbeAsync(u, ct));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static bool TryParseHostPort(string address, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        var idx = address.LastIndexOf(':');
        if (idx <= 0 || idx == address.Length - 1)
            return false;
        host = address[..idx];
        return int.TryParse(address[(idx + 1)..], out port) && port is > 0 and <= 65535;
    }
}
