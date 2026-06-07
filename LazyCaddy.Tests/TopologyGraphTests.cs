using LazyCaddy.Models;
using LazyCaddy.Topology;
using Xunit;

namespace LazyCaddy.Tests;

public class TopologyGraphTests
{
    private static CaddySnapshot Snap(params Route[] routes) => new(
        Status: CaddyStatus.Unknown,
        Routes: routes,
        Certs: System.Array.Empty<Cert>(),
        Upstreams: new[] { new Upstream("127.0.0.1:8090", UpstreamReachability.Up, System.TimeSpan.FromMilliseconds(5), System.Array.Empty<string>()) },
        Metrics: MetricsSnapshot.Unavailable,
        RawConfigJson: "{}",
        Timestamp: System.DateTimeOffset.Now);

    [Fact]
    public void Build_CreatesHostAndUpstreamNodes_AndAnEdgeBetween()
    {
        var g = TopologyGraph.Build(Snap(
            new Route("example.com", "127.0.0.1:8090", true, "active", "{}", "p")));

        Assert.Contains(g.Nodes, n => n.Kind == NodeKind.Host && n.Label == "example.com");
        Assert.Contains(g.Nodes, n => n.Kind == NodeKind.Upstream && n.Label == "127.0.0.1:8090");
        Assert.NotEmpty(g.Edges);
    }

    [Fact]
    public void Build_MarksUpstreamHealth_FromSnapshot()
    {
        var g = TopologyGraph.Build(Snap(
            new Route("h", "127.0.0.1:8090", true, "active", "{}", "p")));
        var up = Assert.Single(g.Nodes, n => n.Kind == NodeKind.Upstream);
        Assert.Equal(NodeHealth.Up, up.Health);
    }

    [Fact]
    public void Build_DedupesSharedUpstream_AcrossRoutes()
    {
        var g = TopologyGraph.Build(Snap(
            new Route("a", "127.0.0.1:8090", true, "active", "{}", "p0"),
            new Route("b", "127.0.0.1:8090", true, "active", "{}", "p1")));
        Assert.Single(g.Nodes, n => n.Kind == NodeKind.Upstream && n.Label == "127.0.0.1:8090");
        Assert.Equal(2, g.Nodes.Count(n => n.Kind == NodeKind.Host));
    }
}
