// -----------------------------------------------------------------------
// LazyCaddy - pure topology model built from a CaddySnapshot.
// Each route becomes a real handler chain: Host -> (Middleware*) ->
// (ReverseProxy -> Upstream | Terminal). Built from the route's parsed
// handler list, so non-proxy routes show their actual terminal handler
// instead of a phantom reverse_proxy. Edges connect the chain in order.
// -----------------------------------------------------------------------

using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.Topology;

// Terminal = a leaf handler that ends a chain but isn't a reverse_proxy
// (file_server, static_response, templates, error, …).
public enum NodeKind { Host, Matcher, Middleware, ReverseProxy, Terminal, Upstream }
public enum NodeHealth { Unknown, Up, Down, Warn }

public sealed record TopoNode(string Id, NodeKind Kind, string Label, NodeHealth Health)
{
    public Route? Route { get; init; }     // set on Host nodes, for edit/detail
    // The route this node belongs to (0-based), so the layout can give each route its own
    // horizontal swim-lane and chains never cross between routes. Shared/deduped upstream
    // nodes belong to no single route → Lane = -1 (placed in the final column, stacked).
    public int Lane { get; init; } = -1;
    // The node's position in its route's chain, used as the layout column: Host = 0, then each
    // successive handler (every middleware, then the terminal/proxy) increments. This gives each
    // middleware in a chain its own column instead of colliding in a shared "middleware" column.
    // Upstreams sit one column past their reverse_proxy.
    public int Column { get; init; }
}

public sealed record TopoEdge(string FromId, string ToId);

public sealed class TopologyGraph
{
    public IReadOnlyList<TopoNode> Nodes { get; }
    public IReadOnlyList<TopoEdge> Edges { get; }

    private TopologyGraph(IReadOnlyList<TopoNode> nodes, IReadOnlyList<TopoEdge> edges)
    { Nodes = nodes; Edges = edges; }

    public static TopologyGraph Build(CaddySnapshot snap)
    {
        var nodes = new List<TopoNode>();
        var edges = new List<TopoEdge>();
        var upstreamHealth = snap.Upstreams.ToDictionary(
            u => u.Address,
            u => u.Reachability switch
            {
                UpstreamReachability.Up => NodeHealth.Up,
                UpstreamReachability.Down => NodeHealth.Down,
                _ => NodeHealth.Unknown
            });

        var upstreamNodeIds = new Dictionary<string, string>(); // address -> node id (dedupe)

        int r = 0;
        foreach (var route in snap.Routes)
        {
            var hostId = $"host:{r}";
            nodes.Add(new TopoNode(hostId, NodeKind.Host, route.HostOrMatch,
                route.Status == "active" ? NodeHealth.Up : NodeHealth.Warn) { Route = route, Lane = r, Column = 0 });

            // Walk the route's real handler chain. Each middleware becomes a node chained
            // in order; a leaf handler terminates the chain (reverse_proxy → upstreams, or
            // a Terminal node for file_server/static_response/etc.). Subroute containers are
            // skipped — ParseHandlers already recurses into them and flattens their handlers.
            // `col` is the chain position (Host = 0); each node takes the next column so a chain
            // of N handlers spreads across N columns rather than colliding.
            var handlers = ParseChainHandlers(route);

            var prevId = hostId; // tail of the chain so far
            bool sawTerminal = false;
            int col = 1;
            int h = 0;
            foreach (var handler in handlers)
            {
                var kind = HandlerCatalog.Lookup(handler.Type).Kind;
                if (kind == HandlerKind.Structural) continue; // subroute container — not a node

                if (kind == HandlerKind.Middleware)
                {
                    var midId = $"mw:{r}:{h}";
                    nodes.Add(new TopoNode(midId, NodeKind.Middleware, handler.Type, NodeHealth.Unknown) { Lane = r, Column = col });
                    edges.Add(new TopoEdge(prevId, midId));
                    prevId = midId;
                }
                else if (handler.Type == "reverse_proxy")
                {
                    var rpId = $"rp:{r}:{h}";
                    nodes.Add(new TopoNode(rpId, NodeKind.ReverseProxy, "reverse_proxy", NodeHealth.Unknown) { Lane = r, Column = col });
                    edges.Add(new TopoEdge(prevId, rpId));
                    AddUpstreams(rpId, route, col + 1, upstreamNodeIds, upstreamHealth, nodes, edges);
                    prevId = rpId;
                    sawTerminal = true;
                }
                else // Leaf (non-proxy) or Unknown terminal handler
                {
                    var termId = $"term:{r}:{h}";
                    nodes.Add(new TopoNode(termId, NodeKind.Terminal, handler.Type, NodeHealth.Unknown) { Lane = r, Column = col });
                    edges.Add(new TopoEdge(prevId, termId));
                    prevId = termId;
                    sawTerminal = true;
                }
                col++;
                h++;
            }

            // Fallback: no handlers parsed (e.g. dummy data carrying only an Upstream string)
            // but an upstream address exists — synthesize the proxy chain so the route isn't
            // a lone Host node.
            if (!sawTerminal && route.Upstream.Length > 0)
            {
                var rpId = $"rp:{r}:fallback";
                nodes.Add(new TopoNode(rpId, NodeKind.ReverseProxy, "reverse_proxy", NodeHealth.Unknown) { Lane = r, Column = col });
                edges.Add(new TopoEdge(prevId, rpId));
                AddUpstreams(rpId, route, col + 1, upstreamNodeIds, upstreamHealth, nodes, edges);
            }

            r++;
        }

        return new TopologyGraph(nodes, edges);
    }

    // Parse the route's handler chain, tolerating malformed/empty JSON (returns nothing).
    private static IReadOnlyList<HandlerDescriptor> ParseChainHandlers(Route route)
    {
        if (string.IsNullOrWhiteSpace(route.RawConfigJson)) return Array.Empty<HandlerDescriptor>();
        try { return RouteModel.ParseHandlers(route.RawConfigJson, route.ConfigPath); }
        catch { return Array.Empty<HandlerDescriptor>(); }
    }

    // Attach the route's upstream addresses (deduped by dial) to a reverse_proxy node, placing
    // each upstream in `column` (one past its proxy). A shared upstream referenced by several
    // proxies takes the rightmost column among them so it sits clear of every proxy.
    private static void AddUpstreams(
        string rpId, Route route, int column,
        Dictionary<string, string> upstreamNodeIds,
        Dictionary<string, NodeHealth> upstreamHealth,
        List<TopoNode> nodes, List<TopoEdge> edges)
    {
        foreach (var dial in route.Upstream.Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            if (!upstreamNodeIds.TryGetValue(dial, out var upId))
            {
                upId = $"up:{dial}";
                upstreamNodeIds[dial] = upId;
                var health = upstreamHealth.TryGetValue(dial, out var hv) ? hv : NodeHealth.Unknown;
                nodes.Add(new TopoNode(upId, NodeKind.Upstream, dial, health) { Column = column });
            }
            else
            {
                // Already created by an earlier proxy — keep it in the rightmost column needed.
                int idx = nodes.FindIndex(n => n.Id == upId);
                if (idx >= 0 && column > nodes[idx].Column)
                    nodes[idx] = nodes[idx] with { Column = column };
            }
            edges.Add(new TopoEdge(rpId, upId));
        }
    }
}
