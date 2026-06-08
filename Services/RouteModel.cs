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

    private static string Summarize(string type, JsonElement h) => type switch
    {
        "reverse_proxy" => h.TryGetProperty("upstreams", out var u) && u.ValueKind == JsonValueKind.Array
            ? string.Join(", ", u.EnumerateArray().Where(x => x.TryGetProperty("dial", out _)).Select(x => x.GetProperty("dial").GetString()))
            : "reverse_proxy",
        "file_server" => h.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String ? $"root={r.GetString()}" : "file_server",
        "static_response" => h.TryGetProperty("status_code", out var s) ? $"status {s}" : "static_response",
        "subroute" => "subroute",
        _ => type,
    };
}
