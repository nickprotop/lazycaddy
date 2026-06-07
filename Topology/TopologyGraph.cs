// -----------------------------------------------------------------------
// LazyCaddy - pure topology model built from a CaddySnapshot.
// Nodes: Host -> (Middleware*) -> ReverseProxy -> Upstream. Edges connect them.
// -----------------------------------------------------------------------

using LazyCaddy.Models;

namespace LazyCaddy.Topology;

public enum NodeKind { Host, Matcher, Middleware, ReverseProxy, Upstream }
public enum NodeHealth { Unknown, Up, Down, Warn }

public sealed record TopoNode(string Id, NodeKind Kind, string Label, NodeHealth Health)
{
    public Route? Route { get; init; }     // set on Host nodes, for edit/detail
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
                route.Status == "active" ? NodeHealth.Up : NodeHealth.Warn) { Route = route });

            var rpId = $"rp:{r}";
            nodes.Add(new TopoNode(rpId, NodeKind.ReverseProxy, "reverse_proxy", NodeHealth.Unknown));
            edges.Add(new TopoEdge(hostId, rpId));

            foreach (var dial in route.Upstream.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                if (!upstreamNodeIds.TryGetValue(dial, out var upId))
                {
                    upId = $"up:{dial}";
                    upstreamNodeIds[dial] = upId;
                    var health = upstreamHealth.TryGetValue(dial, out var h) ? h : NodeHealth.Unknown;
                    nodes.Add(new TopoNode(upId, NodeKind.Upstream, dial, health));
                }
                edges.Add(new TopoEdge(rpId, upId));
            }
            r++;
        }

        return new TopologyGraph(nodes, edges);
    }
}
