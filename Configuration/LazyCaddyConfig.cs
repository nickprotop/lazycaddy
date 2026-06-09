// -----------------------------------------------------------------------
// LazyCaddy - runtime configuration.
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

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

    /// <summary>Caddy's data directory, used to read real certificate expiry from disk
    /// (the admin API doesn't expose it). Defaults to Caddy's standard location; only useful
    /// when LazyCaddy runs on the same host as Caddy.</summary>
    public string CaddyDataDir { get; init; } = LazyCaddy.Services.CertStore.DefaultDataDir();

    /// <summary>Optional explicit access-log file path (overrides config auto-discovery).
    /// Null → discover from the running config (only works for a local Caddy with a file writer).</summary>
    public string? AccessLogPath { get; init; } = null;

    /// <summary>
    /// Snapshot directory scoped to <see cref="AdminApiUrl"/> so snapshots from different
    /// Caddy instances never mix (a restore must not POST one instance's config into another).
    /// </summary>
    public string InstanceSnapshotDir => Path.Combine(SnapshotDir, InstanceSlug(AdminApiUrl));

    /// <summary>
    /// Deterministic, filesystem-safe directory name identifying a Caddy instance by its admin
    /// endpoint. Identity is host:port (scheme-independent: http/https to the same host:port are
    /// the same admin API). The slug is a sanitized, lowercased "{host}_{port}" plus a short hash
    /// of the normalized key, so distinct endpoints never collide even when sanitization would
    /// otherwise flatten them to the same string.
    /// </summary>
    public static string InstanceSlug(string adminApiUrl)
    {
        // Normalize to a canonical "host:port" identity key (scheme-independent).
        string readable, key;
        if (Uri.TryCreate(adminApiUrl?.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            var host = uri.Host.ToLowerInvariant();
            // uri.Port is filled with the scheme default (80/443) for http/https; -1 only for unknown schemes.
            var port = uri.Port >= 0 ? uri.Port : 0;
            key = $"{host}:{port}";
            readable = Sanitize($"{host}_{port}");
        }
        else
        {
            // Unparseable input: fall back to the raw (trimmed, lowercased) string as the key.
            key = (adminApiUrl ?? string.Empty).Trim().ToLowerInvariant();
            readable = Sanitize(key);
        }

        return $"{readable}-{ShortHash(key)}";
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append((char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') ? char.ToLowerInvariant(c) : '-');
        var cleaned = sb.ToString().Trim('-');
        return cleaned.Length == 0 ? "instance" : cleaned;
    }

    private static string ShortHash(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        // 8 hex chars = 32 bits: ample to disambiguate the handful of endpoints a user connects to.
        return Convert.ToHexStringLower(bytes)[..8];
    }

    public static LazyCaddyConfig Default { get; } = new();
}
