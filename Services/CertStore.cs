// -----------------------------------------------------------------------
// LazyCaddy - read real certificate expiry from Caddy's on-disk storage.
//
// Caddy's admin API does NOT expose managed-certificate expiry, but Caddy
// stores the issued certs under <dataDir>/certificates/<issuer>/<domain>/<domain>.crt.
// We parse the X.509 notAfter from there. This only works when LazyCaddy runs on
// the SAME HOST as Caddy (the common TUI case); for a remote --url the file won't
// be found and expiry stays unknown.
// -----------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;

namespace LazyCaddy.Services;

public sealed class CertStore
{
    private readonly string _certificatesDir; // "<dataDir>/certificates"

    // Expiry changes only on renewal (~60 days), so cache reads and re-check infrequently
    // instead of touching the filesystem on every ~5s poll. Negative results (file not found)
    // are cached too, so a remote/missing-storage setup doesn't re-scan dirs every poll.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, (DateTimeOffset? Expiry, DateTime ReadAtUtc)> _cache = new();
    private readonly object _lock = new();

    public CertStore(string caddyDataDir)
        => _certificatesDir = Path.Combine(caddyDataDir, "certificates");

    /// <summary>Resolve Caddy's data dir the way Caddy does: $XDG_DATA_HOME/caddy, else
    /// ~/.local/share/caddy. (CADDY_DATA isn't a real Caddy env var, but honor it if set as a
    /// convenience override.)</summary>
    public static string DefaultDataDir()
    {
        var caddyData = Environment.GetEnvironmentVariable("CADDY_DATA");
        if (!string.IsNullOrEmpty(caddyData)) return caddyData;

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdg)) return Path.Combine(xdg, "caddy");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "caddy");
    }

    /// <summary>Real expiry for a managed subject, or null if no cert file is found / unreadable.
    /// Cached for <see cref="CacheTtl"/>; searches every issuer subdir for "<subject>/<subject>.crt".</summary>
    public DateTimeOffset? ExpiryFor(string subject)
    {
        if (string.IsNullOrEmpty(subject)) return null;

        lock (_lock)
        {
            if (_cache.TryGetValue(subject, out var hit) && DateTime.UtcNow - hit.ReadAtUtc < CacheTtl)
                return hit.Expiry;
        }

        var fresh = ReadExpiryFromDisk(subject);
        lock (_lock) { _cache[subject] = (fresh, DateTime.UtcNow); }
        return fresh;
    }

    private DateTimeOffset? ReadExpiryFromDisk(string subject)
    {
        if (!Directory.Exists(_certificatesDir)) return null;

        // Wildcard subjects (*.example.com) are stored with the leading "*" replaced by "wildcard_".
        var dirName = subject.StartsWith("*.", StringComparison.Ordinal)
            ? "wildcard_" + subject[2..]
            : subject;

        DateTimeOffset? best = null;
        IEnumerable<string> issuerDirs;
        try { issuerDirs = Directory.EnumerateDirectories(_certificatesDir); }
        catch { return null; }

        foreach (var issuerDir in issuerDirs)
        {
            var crt = Path.Combine(issuerDir, dirName, dirName + ".crt");
            var when = ReadNotAfter(crt);
            // Prefer the latest notAfter if the same subject is issued by multiple issuers.
            if (when is { } w && (best is null || w > best)) best = w;
        }
        return best;
    }

    private static DateTimeOffset? ReadNotAfter(string crtPath)
    {
        try
        {
            if (!File.Exists(crtPath)) return null;
            using var cert = X509CertificateLoader.LoadCertificateFromFile(crtPath);
            return new DateTimeOffset(cert.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        }
        catch { return null; }
    }
}
