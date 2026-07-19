// -----------------------------------------------------------------------
// LazyCaddy - plain data DTOs for the Caddy admin API.
//
// These are immutable records built OFF the UI thread by the poll loop and
// only read (never mutated) on the UI thread. Keep them dumb: no behaviour,
// no references to UI types.
// -----------------------------------------------------------------------

using LazyCaddy.Services;

namespace LazyCaddy.Models;

/// <summary>Overall health/identity of the running Caddy instance.</summary>
public sealed record CaddyStatus(
    bool Running,
    string Version,
    TimeSpan Uptime,
    int RouteCount,
    int CertValidCount,
    int CertExpiringCount,
    int UpstreamUpCount,
    int UpstreamDownCount)
{
    public static CaddyStatus Unknown => new(
        Running: false,
        Version: "unknown",
        Uptime: TimeSpan.Zero,
        RouteCount: 0,
        CertValidCount: 0,
        CertExpiringCount: 0,
        UpstreamUpCount: 0,
        UpstreamDownCount: 0);
}

/// <summary>One routing rule: public host/matcher -> internal upstream.</summary>
public sealed record Route(
    string HostOrMatch,
    string Upstream,
    bool TlsEnabled,
    string Status,
    // Pretty-printed JSON of this route's matcher+handler config, for the detail overlay.
    string RawConfigJson,
    // Admin-API path of this route node, e.g. "apps/http/servers/srv0/routes/0".
    // Empty when unknown (e.g. dummy data). Used by Phase B for granular PATCH.
    string ConfigPath = "",
    // Server name so that duplicate hosts on different servers can be distinguished in the UI.
    string ServerName = "",
    // Comma-separated listen addresses from this server, e.g. ":8443"
    string Listen = "");

/// <summary>An http server in the config, for pickers that must name a target server.</summary>
/// <param name="Name">The server's key under apps/http/servers, e.g. "srv0".</param>
/// <param name="Listen">Comma-separated listen addresses, e.g. ":8443". Empty when absent.</param>
public sealed record ServerInfo(string Name, string Listen)
{
    /// <summary>Human label for a picker: "srv0 — :8443", or just the name when listen is unknown.</summary>
    public string Label => string.IsNullOrEmpty(Listen) ? Name : $"{Name} — {Listen}";

    /// <summary>Admin-API path of this server, e.g. "apps/http/servers/srv0".</summary>
    public string ConfigPath => $"apps/http/servers/{Name}";
}

/// <summary>A TLS certificate managed by (or loaded into) Caddy.</summary>
public sealed record Cert(
    string Domain,
    string Issuer,
    DateTimeOffset Expires,
    string AcmeStatus,
    // False when the real expiry couldn't be read (Caddy's admin API doesn't expose it; we read
    // the cert file from disk, which only works when LazyCaddy runs on the same host as Caddy).
    // When false, Expires is meaningless and the UI shows "unknown" instead of a days-left value.
    bool ExpiryKnown = true)
{
    /// <summary>Whole days until expiry from <paramref name="now" /> (floored, may be negative).</summary>
    public int DaysLeft(DateTimeOffset now) => (int)Math.Floor((Expires - now).TotalDays);
}

/// <summary>Reachability state of a single upstream, including the active-probe result.</summary>
public enum UpstreamReachability { Unknown, Probing, Up, Down }

public sealed record Upstream(
    string Address,
    UpstreamReachability Reachability,
    TimeSpan? Latency,
    IReadOnlyList<string> UsedByRoutes)
{
    public Upstream WithProbe(UpstreamReachability reachability, TimeSpan? latency) =>
        this with { Reachability = reachability, Latency = latency };
}

/// <summary>Request-rate-over-time series for the optional Overview sparkline.</summary>
/// <remarks>
/// Sourced from Caddy's Prometheus <c>/metrics</c> endpoint, which is not guaranteed
/// to be enabled. <see cref="Available" /> is false when metrics could not be read; the
/// Overview view hides the sparkline card in that case.
/// </remarks>
public sealed record MetricsSnapshot(
    bool Available,
    IReadOnlyList<double> RequestRate,
    StatusClassCounts StatusClasses,
    double InFlight,
    LatencyPercentiles Latency,
    IReadOnlyList<LabelCount> TopHandlers)
{
    public static MetricsSnapshot Unavailable => new(
        false, Array.Empty<double>(), default, 0, LatencyPercentiles.Unavailable, Array.Empty<LabelCount>());
}

/// <summary>The full set of data produced by one poll, plus a timestamp.</summary>
public sealed record CaddySnapshot(
    CaddyStatus Status,
    IReadOnlyList<Route> Routes,
    IReadOnlyList<Cert> Certs,
    IReadOnlyList<Upstream> Upstreams,
    MetricsSnapshot Metrics,
    string RawConfigJson,
    DateTimeOffset Timestamp);
