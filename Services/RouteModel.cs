// -----------------------------------------------------------------------
// LazyCaddy - pure parse of a route's handle[] into a flat list of handler
// descriptors, recursing through subroute. Each descriptor carries the exact
// admin-API config path of that handler node, for granular editing.
// -----------------------------------------------------------------------

using System.Text.Json;

namespace LazyCaddy.Services;

/// <summary>One handler in a route's chain. Depth>0 means it's inside a subroute.</summary>
public sealed record HandlerDescriptor(string Type, string ConfigPath, int Depth, string Summary);

public static class RouteModel
{
    /// <summary>Flatten a route node's handlers (recursing subroutes) to descriptors.</summary>
    public static IReadOnlyList<HandlerDescriptor> ParseHandlers(string routeJson, string routeConfigPath)
    {
        var result = new List<HandlerDescriptor>();
        using var doc = JsonDocument.Parse(routeJson);
        Walk(doc.RootElement, routeConfigPath, 0, result);
        return result;
    }

    private static void Walk(JsonElement routeNode, string path, int depth, List<HandlerDescriptor> acc)
    {
        if (!routeNode.TryGetProperty("handle", out var handlers) || handlers.ValueKind != JsonValueKind.Array)
            return;

        int i = 0;
        foreach (var h in handlers.EnumerateArray())
        {
            var type = h.TryGetProperty("handler", out var hn) && hn.ValueKind == JsonValueKind.String
                ? hn.GetString()! : "(unknown)";
            var hPath = $"{path}/handle/{i}";
            acc.Add(new HandlerDescriptor(type, hPath, depth, Summarize(type, h)));

            if (type == "subroute" && h.TryGetProperty("routes", out var subs) && subs.ValueKind == JsonValueKind.Array)
            {
                int j = 0;
                foreach (var sr in subs.EnumerateArray())
                {
                    Walk(sr, $"{hPath}/routes/{j}", depth + 1, acc);
                    j++;
                }
            }
            i++;
        }
    }

    // A one-line, human-readable description of what a handler does, pulled from its key fields.
    // Falls back to the bare type name when there's nothing distinctive to show.
    private static string Summarize(string type, JsonElement h) => type switch
    {
        "reverse_proxy" => SummarizeReverseProxy(h),
        "file_server" => h.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String
            ? $"root {r.GetString()}"
            : (HasTrue(h, "browse") || h.TryGetProperty("browse", out var b) && b.ValueKind == JsonValueKind.Object ? "directory browsing" : "file_server"),
        "static_response" => SummarizeStaticResponse(h),
        "rewrite" => SummarizeRewrite(h),
        "headers" => SummarizeHeaders(h),
        "encode" => h.TryGetProperty("encodings", out var enc) && enc.ValueKind == JsonValueKind.Object
            ? "encode " + string.Join("/", enc.EnumerateObject().Select(p => p.Name))
            : "compress responses",
        "error" => h.TryGetProperty("status_code", out var ec) ? $"error {Scalar(ec)}" : "raise error",
        "vars" => h.EnumerateObject().Any(p => p.Name != "handler")
            ? "set " + string.Join(", ", h.EnumerateObject().Where(p => p.Name != "handler").Select(p => p.Name))
            : "set vars",
        "request_body" => h.TryGetProperty("max_size", out var ms) ? $"max body {Scalar(ms)}" : "limit request body",
        "templates" => "render templates",
        "authentication" => SummarizeAuth(h),
        "subroute" => SummarizeSubroute(h),
        _ => type,
    };

    private static string SummarizeReverseProxy(JsonElement h)
    {
        if (!h.TryGetProperty("upstreams", out var u) || u.ValueKind != JsonValueKind.Array)
            return "reverse_proxy (no upstreams)";
        var dials = u.EnumerateArray()
            .Where(x => x.TryGetProperty("dial", out _))
            .Select(x => x.GetProperty("dial").GetString())
            .Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (dials.Count == 0) return "reverse_proxy (no upstreams)";
        return dials.Count <= 2
            ? "→ " + string.Join(", ", dials)
            : $"→ {dials[0]} +{dials.Count - 1} more";
    }

    private static string SummarizeStaticResponse(JsonElement h)
    {
        var status = h.TryGetProperty("status_code", out var s) ? Scalar(s) : null;
        // A static_response with a Location header is a redirect.
        if (h.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Object
            && hdrs.TryGetProperty("Location", out var loc))
        {
            var to = loc.ValueKind == JsonValueKind.Array && loc.GetArrayLength() > 0 ? loc[0].GetString() : Scalar(loc);
            return status is not null ? $"redirect {status} → {to}" : $"redirect → {to}";
        }
        if (h.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String)
        {
            var txt = body.GetString() ?? "";
            var trimmed = txt.Length > 24 ? txt[..24] + "…" : txt;
            return status is not null ? $"respond {status} \"{trimmed}\"" : $"respond \"{trimmed}\"";
        }
        return status is not null ? $"respond {status}" : "static_response";
    }

    private static string SummarizeRewrite(JsonElement h)
    {
        if (h.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String) return $"uri → {uri.GetString()}";
        if (h.TryGetProperty("strip_path_prefix", out var sp) && sp.ValueKind == JsonValueKind.String) return $"strip prefix {sp.GetString()}";
        if (h.TryGetProperty("strip_path_suffix", out var ss) && ss.ValueKind == JsonValueKind.String) return $"strip suffix {ss.GetString()}";
        if (h.TryGetProperty("path_regexp", out _)) return "regex path rewrite";
        return "rewrite request";
    }

    private static string SummarizeHeaders(JsonElement h)
    {
        var parts = new List<string>();
        CountHeaderOps(h, "request", "req", parts);
        CountHeaderOps(h, "response", "resp", parts);
        return parts.Count > 0 ? "headers " + string.Join(", ", parts) : "modify headers";
    }

    // Count set/add/delete operations under a request/response header block, e.g. "req +2 -1".
    private static void CountHeaderOps(JsonElement h, string prop, string label, List<string> into)
    {
        if (!h.TryGetProperty(prop, out var blk) || blk.ValueKind != JsonValueKind.Object) return;
        int adds = ObjCount(blk, "set") + ObjCount(blk, "add");
        int dels = ArrCount(blk, "delete");
        if (adds == 0 && dels == 0) return;
        var s = label;
        if (adds > 0) s += $" +{adds}";
        if (dels > 0) s += $" -{dels}";
        into.Add(s);
    }

    private static string SummarizeAuth(JsonElement h)
    {
        if (h.TryGetProperty("providers", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            var names = p.EnumerateObject().Select(o => o.Name).ToList();
            if (names.Count > 0) return "auth " + string.Join(", ", names);
        }
        return "authentication";
    }

    private static string SummarizeSubroute(JsonElement h)
    {
        if (h.TryGetProperty("routes", out var routes) && routes.ValueKind == JsonValueKind.Array)
        {
            var n = routes.GetArrayLength();
            return n == 1 ? "subroute (1 route)" : $"subroute ({n} routes)";
        }
        return "subroute";
    }

    // --- small JSON helpers ---
    private static bool HasTrue(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static int ObjCount(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v.EnumerateObject().Count() : 0;
    private static int ArrCount(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : 0;
    private static string Scalar(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText();
}
