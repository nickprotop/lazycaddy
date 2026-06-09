using System.Text.Json;

namespace LazyCaddy.Services;

/// <summary>
/// Minimal-but-valid handler skeletons for creating a route/handler of a chosen type,
/// plus the offered-types list shared by the new-route wizard and the add-handler action.
/// `redir` is synthetic — Caddy has no distinct redir JSON handler; it maps to a
/// static_response with a Location header + 302 (matching how the Caddyfile compiles it).
/// </summary>
public static class NewRouteSkeleton
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    /// <summary>An offered handler type for creation: the JSON `Type`, display name, and icon.</summary>
    public sealed record Offered(string Type, string DisplayName, string Icon);

    // Leaf + middleware handlers offered by the new-route wizard (from HandlerCatalog, minus
    // subroute, minus forward_auth — which has no MinimalHandler and is only added via its modal
    // on existing routes), plus the synthetic redir.
    public static readonly IReadOnlyList<Offered> OfferedTypes = BuildOffered();

    // Types offered by the add-handler picker on an existing route. Same as the wizard's set, but
    // also includes forward_auth — the picker maps it to its modal rather than a MinimalHandler.
    public static readonly IReadOnlyList<Offered> PickerTypes = BuildPicker();

    private static IReadOnlyList<Offered> BuildOffered()
    {
        var list = HandlerCatalog.All
            .Where(h => h.Kind is HandlerKind.Leaf or HandlerKind.Middleware
                        && h.Type != "subroute" && h.Type != "forward_auth")
            .Select(h => new Offered(h.Type, h.DisplayName, h.Icon))
            .ToList();
        list.Add(new Offered("redir", "Redirect", "⇲"));
        return list;
    }

    private static IReadOnlyList<Offered> BuildPicker()
    {
        var list = HandlerCatalog.All
            .Where(h => h.Kind is HandlerKind.Leaf or HandlerKind.Middleware && h.Type != "subroute")
            .Select(h => new Offered(h.Type, h.DisplayName, h.Icon))
            .ToList();
        list.Add(new Offered("redir", "Redirect", "⇲"));
        return list;
    }

    /// <summary>The form to open for a chosen type — `redir` edits via the static_response form.</summary>
    public static string FormType(string type) => type == "redir" ? "static_response" : type;

    /// <summary>Minimal valid handler JSON for the chosen type.</summary>
    public static string MinimalHandler(string type)
    {
        Dictionary<string, object> o = type switch
        {
            "reverse_proxy" => new() { ["handler"] = "reverse_proxy", ["upstreams"] = System.Array.Empty<object>() },
            "redir" => new()
            {
                ["handler"] = "static_response",
                ["headers"] = new Dictionary<string, object> { ["Location"] = new[] { "http://example.com" } },
                ["status_code"] = 302,
            },
            "rate_limit" => new() { ["handler"] = "rate_limit", ["rate_limits"] = new Dictionary<string, object>() },
            _ => new() { ["handler"] = type },
        };
        return JsonSerializer.Serialize(o, Opt);
    }
}
