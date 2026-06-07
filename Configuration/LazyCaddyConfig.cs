// -----------------------------------------------------------------------
// LazyCaddy - runtime configuration.
// -----------------------------------------------------------------------

namespace LazyCaddy.Configuration;

/// <summary>
/// Immutable runtime configuration for the dashboard. Use <see cref="Default"/> for
/// the standard local-Caddy setup, or construct with overrides.
/// </summary>
public sealed record LazyCaddyConfig
{
    /// <summary>Caddy admin API base URL.</summary>
    public string AdminApiUrl { get; init; } = "http://localhost:2019";

    /// <summary>How often the background thread polls the admin API, in milliseconds.</summary>
    public int RefreshIntervalMs { get; init; } = 5000;

    /// <summary>Per-call HTTP timeout against the admin API, in milliseconds.</summary>
    public int HttpTimeoutMs { get; init; } = 4000;

    /// <summary>Per-upstream active reachability probe timeout, in milliseconds.</summary>
    public int ProbeTimeoutMs { get; init; } = 1500;

    /// <summary>
    /// Whether to build the Overview request-rate sparkline. Default on; if the live
    /// /metrics endpoint is unavailable the card hides itself gracefully.
    /// </summary>
    public bool EnableRequestRateSparkline { get; init; } = true;

    /// <summary>Max points retained in request-rate history (sparkline width budget).</summary>
    public int MaxHistoryPoints { get; init; } = 50;

    /// <summary>Directory for persisted config snapshots.</summary>
    public string SnapshotDir { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "lazycaddy", "snapshots");

    /// <summary>Max auto-snapshots retained (pinned ones don't count and aren't dropped).</summary>
    public int MaxAutoSnapshots { get; init; } = 50;

    /// <summary>When true, all writes are blocked (safety/demo).</summary>
    public bool ReadOnly { get; init; } = false;

    public static LazyCaddyConfig Default { get; } = new();
}
