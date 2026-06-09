// -----------------------------------------------------------------------
// LazyCaddy - turn Caddy's verbose write/load error bodies into a concise,
// plain-English line for the UI. Pure string work, no HTTP/UI.
//
// Caddy rejects bad writes server-side (and rolls back), returning errors like:
//   {"error":"loading config: loading new config: ... route 0: loading handler
//    modules: position 0: loading module 'foo': unknown module: http.handlers.foo"}
// We strip the boilerplate prefix chain, keep the meaningful tail (and any
// "route N" context), and map a few common patterns to friendlier wording.
// Unknown shapes fall back to the trimmed raw error — never hide information.
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;

namespace LazyCaddy.Services;

public static partial class CaddyErrorFormatter
{
    private const int MaxLen = 200;

    public static string Format(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return "Caddy rejected the change (no detail provided).";

        var raw = Unwrap(rawBody.Trim());

        // Pull a "route N" context if present, to prefix the friendly message.
        var routePrefix = "";
        var routeMatch = RouteRx().Match(raw);
        if (routeMatch.Success) routePrefix = $"Route {routeMatch.Groups[1].Value}: ";

        // unknown module: http.handlers.foo  → Unknown handler 'foo'
        var unknownMod = UnknownModuleRx().Match(raw);
        if (unknownMod.Success)
        {
            var full = unknownMod.Groups[1].Value;            // e.g. http.handlers.foo
            var kind = full.Contains(".handlers.") ? "handler"
                : full.Contains(".matchers.") ? "matcher"
                : "module";
            var name = full[(full.LastIndexOf('.') + 1)..];
            return Cap($"{routePrefix}Unknown {kind} '{name}'");
        }

        // json: cannot unmarshal X into ... of type Y  → Wrong type: expected a <Y>
        var unmarshal = UnmarshalRx().Match(raw);
        if (unmarshal.Success)
        {
            var goType = unmarshal.Groups[1].Value;
            var friendly = goType switch
            {
                "int" or "int64" or "uint" or "float64" => "a number",
                "bool" => "true/false",
                "string" => "text",
                _ => $"type {goType}",
            };
            return Cap($"{routePrefix}Wrong type: expected {friendly}");
        }

        // Otherwise: drop the boilerplate prefix chain, keep the most specific tail segment.
        return Cap(routePrefix.Length > 0 ? routePrefix + Tail(raw) : Tail(raw));
    }

    // {"error":"..."} → the inner message; otherwise the input unchanged.
    private static string Unwrap(string body)
    {
        if (body.Length == 0 || body[0] != '{') return body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                return e.GetString()!.Trim();
        }
        catch { /* not JSON; use as-is */ }
        return body;
    }

    // Take the last ": "-delimited segment that still carries meaning (the actual cause),
    // skipping generic Go wrapper phrases.
    private static string Tail(string raw)
    {
        var parts = raw.Split(": ", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return raw;
        // The final segment is usually the root cause.
        return parts[^1].Trim();
    }

    private static string Cap(string s) => s.Length <= MaxLen ? s : s[..(MaxLen - 1)] + "…";

    [GeneratedRegex(@"unknown module: ([a-zA-Z0-9_.]+)")]
    private static partial Regex UnknownModuleRx();

    [GeneratedRegex(@"\broute (\d+)\b")]
    private static partial Regex RouteRx();

    [GeneratedRegex(@"unmarshal .*? of type (\w+)")]
    private static partial Regex UnmarshalRx();
}
