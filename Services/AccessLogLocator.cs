// -----------------------------------------------------------------------
// LazyCaddy - pure resolver: from the running config (+ an optional override
// path + whether the admin URL is local) decide where the access log file is,
// or why it can't be read. No I/O — the existence/permission of the path is the
// tailer's concern.
// -----------------------------------------------------------------------

using System.Text.Json;

namespace LazyCaddy.Services;

public enum LogSourceKind { File, NotConfigured, Remote }

/// <summary>Where (or why not) to read access logs. Path is set only for File.</summary>
public readonly record struct LogSource(LogSourceKind Kind, string? Path)
{
    public static LogSource File(string path) => new(LogSourceKind.File, path);
    public static readonly LogSource NotConfigured = new(LogSourceKind.NotConfigured, null);
    public static readonly LogSource Remote = new(LogSourceKind.Remote, null);
}

public static class AccessLogLocator
{
    /// <summary>Resolve the access-log source. Override path always wins; otherwise a remote
    /// admin URL yields Remote; otherwise read the config to find a server's file writer.</summary>
    public static LogSource Resolve(string configJson, string? overridePath, bool urlIsLocal)
    {
        if (!string.IsNullOrWhiteSpace(overridePath)) return LogSource.File(overridePath!);
        if (!urlIsLocal) return LogSource.Remote;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            if (!TryGet(root, out var servers, "apps", "http", "servers") ||
                servers.ValueKind != JsonValueKind.Object)
                return LogSource.NotConfigured;

            foreach (var server in servers.EnumerateObject())
            {
                if (!server.Value.TryGetProperty("logs", out var logs) || logs.ValueKind != JsonValueKind.Object)
                    continue;
                if (!logs.TryGetProperty("default_logger_name", out var lname) || lname.ValueKind != JsonValueKind.String)
                    continue;
                var name = lname.GetString()!;
                if (TryGet(root, out var writer, "logging", "logs", name, "writer") &&
                    writer.ValueKind == JsonValueKind.Object &&
                    writer.TryGetProperty("output", out var output) && output.GetString() == "file" &&
                    writer.TryGetProperty("filename", out var fn) && fn.ValueKind == JsonValueKind.String)
                {
                    return LogSource.File(fn.GetString()!);
                }
            }
        }
        catch (JsonException) { /* fall through */ }

        return LogSource.NotConfigured;
    }

    /// <summary>True when the admin URL host is loopback (so the log file is on this machine).</summary>
    public static bool UrlIsLocal(string adminApiUrl)
    {
        if (!Uri.TryCreate(adminApiUrl, UriKind.Absolute, out var uri)) return false;
        var h = uri.Host.Trim('[', ']').ToLowerInvariant();
        return h is "localhost" or "127.0.0.1" or "::1";
    }

    private static bool TryGet(JsonElement root, out JsonElement found, params string[] path)
    {
        found = root;
        foreach (var key in path)
        {
            if (found.ValueKind != JsonValueKind.Object || !found.TryGetProperty(key, out found))
            { found = default; return false; }
        }
        return true;
    }
}
