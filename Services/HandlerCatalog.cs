// -----------------------------------------------------------------------
// LazyCaddy - registry of Caddy HTTP handler types: kind, display name, icon.
// Drives the route editor's "add handler" picker and handler-list rendering.
// -----------------------------------------------------------------------

namespace LazyCaddy.Services;

public enum HandlerKind { Leaf, Middleware, Structural, Unknown }

public sealed record HandlerInfo(string Type, HandlerKind Kind, string DisplayName, string Icon);

public static class HandlerCatalog
{
    // Ordered for the "add handler" picker. Forms exist for some; others use raw edit.
    public static readonly IReadOnlyList<HandlerInfo> All = new[]
    {
        new HandlerInfo("reverse_proxy",   HandlerKind.Leaf,       "Reverse proxy",   "↦"),
        new HandlerInfo("file_server",     HandlerKind.Leaf,       "File server",     "📁"),
        new HandlerInfo("static_response", HandlerKind.Leaf,       "Static response", "▦"),
        new HandlerInfo("templates",       HandlerKind.Leaf,       "Templates",       "🖹"),
        new HandlerInfo("error",           HandlerKind.Leaf,       "Error",           "✕"),
        new HandlerInfo("rewrite",         HandlerKind.Middleware, "Rewrite",         "✎"),
        new HandlerInfo("headers",         HandlerKind.Middleware, "Headers",         "≡"),
        new HandlerInfo("encode",          HandlerKind.Middleware, "Encode",          "⇄"),
        new HandlerInfo("vars",            HandlerKind.Middleware, "Vars",            "𝑥"),
        new HandlerInfo("request_body",    HandlerKind.Middleware, "Request body",    "⤓"),
        new HandlerInfo("authentication",  HandlerKind.Middleware, "Authentication",  "🔑"),
        new HandlerInfo("rate_limit",      HandlerKind.Middleware, "Rate limit",      "⏱"),
        new HandlerInfo("forward_auth",    HandlerKind.Middleware, "Forward auth",    "🛡"),
        new HandlerInfo("subroute",        HandlerKind.Structural, "Subroute",        "⊞"),
    };

    public static HandlerInfo Lookup(string type) =>
        All.FirstOrDefault(h => h.Type == type)
        ?? new HandlerInfo(type, HandlerKind.Unknown, type, "?");
}
