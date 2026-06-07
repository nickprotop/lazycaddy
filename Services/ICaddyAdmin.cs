// -----------------------------------------------------------------------
// LazyCaddy - abstraction over the Caddy admin API HTTP calls.
//
// All methods are async and run on the background poll thread. Implementations
// must not touch any UI control.
// -----------------------------------------------------------------------

using LazyCaddy.Models;

namespace LazyCaddy.Services;

public interface ICaddyAdmin
{
    /// <summary>High-level status: running, version, uptime, and rollup counts.</summary>
    Task<CaddyStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Routes flattened to public host/matcher -> upstream rows.</summary>
    Task<IReadOnlyList<Route>> GetRoutesAsync(CancellationToken ct = default);

    /// <summary>Managed/loaded TLS certificates.</summary>
    Task<IReadOnlyList<Cert>> GetCertsAsync(CancellationToken ct = default);

    /// <summary>Distinct upstreams referenced by the config (reachability filled in separately).</summary>
    Task<IReadOnlyList<Upstream>> GetUpstreamsAsync(CancellationToken ct = default);

    /// <summary>Optional request-rate metrics from the Prometheus /metrics endpoint.</summary>
    Task<MetricsSnapshot> GetMetricsAsync(CancellationToken ct = default);

    /// <summary>The full running config as a pretty-printed JSON string.</summary>
    Task<string> GetRawConfigAsync(CancellationToken ct = default);

    /// <summary>GET a single config node as raw JSON, e.g. "apps/http/servers/srv0/routes/0/match".</summary>
    Task<string> GetConfigNodeAsync(string path, CancellationToken ct = default);

    /// <summary>PATCH (replace) a single config node with the given JSON.</summary>
    Task<WriteResult> PatchConfigAsync(string path, string json, CancellationToken ct = default);

    /// <summary>POST (append to an array) at a config path.</summary>
    Task<WriteResult> PostConfigAsync(string path, string json, CancellationToken ct = default);

    /// <summary>DELETE a config node.</summary>
    Task<WriteResult> DeleteConfigAsync(string path, CancellationToken ct = default);

    /// <summary>Replace the entire running config (POST /load).</summary>
    Task<WriteResult> LoadConfigAsync(string fullConfigJson, CancellationToken ct = default);
}

/// <summary>Outcome of a write: success, or Caddy's verbatim error body.</summary>
public sealed record WriteResult(bool Success, string? Error)
{
    public static WriteResult Ok => new(true, null);
    public static WriteResult Fail(string error) => new(false, error);
}
