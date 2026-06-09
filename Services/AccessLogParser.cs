// -----------------------------------------------------------------------
// LazyCaddy - pure parser for one Caddy JSON access-log line. No I/O.
// A line is an "access entry" only if it has both a request object and a
// status; otherwise it's treated as raw (shown, not dropped). Blank → null.
// -----------------------------------------------------------------------

using System.Text.Json;
using LazyCaddy.Models;

namespace LazyCaddy.Services;

public static class AccessLogParser
{
    /// <summary>Parse one log line. Null for blank lines; a raw entry for non-access or
    /// unparseable lines; a populated entry for a Caddy JSON access line.</summary>
    public static AccessLogEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var trimmed = line.Trim();

        if (trimmed[0] != '{') return Raw(trimmed);
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Raw(trimmed);

            if (!root.TryGetProperty("request", out var req) || req.ValueKind != JsonValueKind.Object)
                return Raw(trimmed);
            if (!root.TryGetProperty("status", out var statusEl) || statusEl.ValueKind != JsonValueKind.Number)
                return Raw(trimmed);

            var time = root.TryGetProperty("ts", out var ts) && ts.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)(ts.GetDouble() * 1000.0))
                : DateTimeOffset.UtcNow;

            return new AccessLogEntry(
                Time: time,
                Status: statusEl.GetInt32(),
                Method: Str(req, "method"),
                Host: Str(req, "host"),
                Uri: Str(req, "uri"),
                DurationSeconds: Num(root, "duration"),
                Size: (long)Num(root, "size"));
        }
        catch (JsonException)
        {
            return Raw(trimmed);
        }
    }

    private static AccessLogEntry Raw(string line) =>
        new(DateTimeOffset.UtcNow, 0, "", "", "", 0, 0, Raw: line);

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0d;
}
