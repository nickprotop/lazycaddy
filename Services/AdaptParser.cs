// -----------------------------------------------------------------------
// LazyCaddy - pure parser for Caddy's POST /adapt response.
//
// /adapt converts a Caddyfile to JSON without touching the running config.
// Success (200): {"warnings":[{file,line,message}],"result":{...full config...}}
// Failure (4xx): {"error":"...adapter syntax error..."}
// This is HTTP-free: it takes the raw response body + whether the HTTP call
// succeeded, and produces a structured result the view can render.
// -----------------------------------------------------------------------

using System.Text.Json;

namespace LazyCaddy.Services;

/// <summary>One non-fatal adapter warning (e.g. "not formatted").</summary>
public readonly record struct AdaptWarning(string File, int Line, string Message);

/// <summary>Outcome of a Caddyfile→JSON adaptation.</summary>
public sealed record AdaptResult(
    bool Success,
    string? ResultJson,                  // pretty-printed adapted config (when Success)
    string? Error,                       // adapter error message (when !Success)
    IReadOnlyList<AdaptWarning> Warnings)
{
    public static AdaptResult Fail(string error) => new(false, null, error, Array.Empty<AdaptWarning>());
}

public static class AdaptParser
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>Parse the /adapt response body. <paramref name="httpOk"/> is the HTTP success flag;
    /// even on a 200 the body must contain a "result" object to be a success.</summary>
    public static AdaptResult Parse(bool httpOk, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return AdaptResult.Fail(httpOk ? "Empty response from /adapt." : "Request to /adapt failed.");

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
        }
        catch
        {
            // Non-JSON body (rare) — surface it verbatim as the error/result.
            return httpOk ? new AdaptResult(true, body, null, Array.Empty<AdaptWarning>()) : AdaptResult.Fail(body);
        }

        // Error shape: {"error":"..."}
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var err))
            return AdaptResult.Fail(err.GetString() ?? "Adapter error.");

        if (!httpOk)
            return AdaptResult.Fail("Request to /adapt failed.");

        // Success shape: {"warnings":[...],"result":{...}}
        var warnings = ParseWarnings(root);
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result))
            return new AdaptResult(true, JsonSerializer.Serialize(result, Pretty), null, warnings);

        // Some Caddy versions may return the bare config as the root.
        return new AdaptResult(true, JsonSerializer.Serialize(root, Pretty), null, warnings);
    }

    private static IReadOnlyList<AdaptWarning> ParseWarnings(JsonElement root)
    {
        var list = new List<AdaptWarning>();
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("warnings", out var ws) && ws.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in ws.EnumerateArray())
            {
                var file = w.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString()! : "";
                var line = w.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0;
                var msg = w.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : "";
                list.Add(new AdaptWarning(file, line, msg));
            }
        }
        return list;
    }
}
