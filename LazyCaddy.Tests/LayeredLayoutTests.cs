using LazyCaddy.Models;
using LazyCaddy.Topology;
using Xunit;

namespace LazyCaddy.Tests;

public class LayeredLayoutTests
{
    private static TopologyGraph TwoRouteGraph()
    {
        var snap = new CaddySnapshot(
            CaddyStatus.Unknown,
            new[]
            {
                new Route("a.example", "127.0.0.1:1", true, "active", "{}", "p0"),
                new Route("b.example", "127.0.0.1:2", true, "active", "{}", "p1"),
            },
            System.Array.Empty<Cert>(),
            System.Array.Empty<Upstream>(),
            MetricsSnapshot.Unavailable, "{}", System.DateTimeOffset.Now);
        return TopologyGraph.Build(snap);
    }

    [Fact]
    public void Layout_PlacesHostsInLayer0_AndUpstreamsInLastLayer()
    {
        var placed = LayeredLayout.Arrange(TwoRouteGraph(), boxWidth: 16, boxHeight: 3, hGap: 4, vGap: 1);
        var hostX = placed.Where(p => p.Node.Kind == NodeKind.Host).Select(p => p.X).Distinct().ToList();
        var upX = placed.Where(p => p.Node.Kind == NodeKind.Upstream).Select(p => p.X).Distinct().ToList();
        Assert.Single(hostX);                       // all hosts share the leftmost column
        Assert.True(upX.Max() > hostX[0]);          // upstreams are to the right of hosts
    }

    [Fact]
    public void Layout_DoesNotOverlapNodesVertically_InSameLayer()
    {
        var placed = LayeredLayout.Arrange(TwoRouteGraph(), 16, 3, 4, 1);
        var hosts = placed.Where(p => p.Node.Kind == NodeKind.Host).OrderBy(p => p.Y).ToList();
        Assert.True(hosts[1].Y >= hosts[0].Y + 3);  // second host below first by >= boxHeight
    }
}
