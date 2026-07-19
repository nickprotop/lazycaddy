using System.Linq;
using LazyCaddy.Models;
using LazyCaddy.Topology;
using Xunit;

namespace LazyCaddy.Tests;

public class LayeredLayoutTests
{
    private static string ProxyJson(string dial) =>
        $$"""{"handle":[{"handler":"reverse_proxy","upstreams":[{"dial":"{{dial}}"}]}]}""";

    private static TopologyGraph TwoRouteGraph()
    {
        var snap = new CaddySnapshot(
            CaddyStatus.Unknown,
            new[]
            {
                new Route("a.example", "127.0.0.1:1", true, "active", ProxyJson("127.0.0.1:1"), "p0", "", ""),
                new Route("b.example", "127.0.0.1:2", true, "active", ProxyJson("127.0.0.1:2"), "p1"),
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

    // ── Swim-lanes: each route gets its own horizontal band ──────────────

    private static TopologyGraph MixedChainGraph()
    {
        // Route 0: authentication → reverse_proxy → up1   (one middleware)
        // Route 1: reverse_proxy → up2                     (no middleware)
        var r0 = """
        {"handle":[
            {"handler":"authentication","providers":{"http_basic":{}}},
            {"handler":"reverse_proxy","upstreams":[{"dial":"127.0.0.1:1"}]}
        ]}
        """;
        var snap = new CaddySnapshot(
            CaddyStatus.Unknown,
            new[]
            {
                new Route("a.example", "127.0.0.1:1", true, "active", r0, "p0"),
                new Route("b.example", "127.0.0.1:2", true, "active", ProxyJson("127.0.0.1:2"), "p1"),
            },
            System.Array.Empty<Cert>(),
            System.Array.Empty<Upstream>(),
            MetricsSnapshot.Unavailable, "{}", System.DateTimeOffset.Now);
        return TopologyGraph.Build(snap);
    }

    [Fact]
    public void Layout_KeepsEachRoutesChainOnItsOwnRow()
    {
        var placed = LayeredLayout.Arrange(MixedChainGraph(), 16, 3, 4, 1)
            .ToDictionary(p => p.Node.Id, p => p);

        // Route 0's host, its middleware, and its reverse_proxy share one Y (one swim-lane).
        var host0 = placed.Values.Single(p => p.Node.Kind == NodeKind.Host && p.Node.Lane == 0);
        var mid0 = placed.Values.Single(p => p.Node.Kind == NodeKind.Middleware && p.Node.Lane == 0);
        var rp0 = placed.Values.Single(p => p.Node.Kind == NodeKind.ReverseProxy && p.Node.Lane == 0);
        Assert.Equal(host0.Y, mid0.Y);
        Assert.Equal(host0.Y, rp0.Y);

        // Route 1 (no middleware) sits on a different row from route 0.
        var host1 = placed.Values.Single(p => p.Node.Kind == NodeKind.Host && p.Node.Lane == 1);
        Assert.NotEqual(host0.Y, host1.Y);

        // Route 1's reverse_proxy shares route 1's row — NOT route 0's. This is the assertion
        // the old order-within-layer layout fails: route 0's chain is longer, pushing its proxy
        // into a layer-row index that no longer matches route 1's host row.
        var rp1 = placed.Values.Single(p => p.Node.Kind == NodeKind.ReverseProxy && p.Node.Lane == 1);
        Assert.Equal(host1.Y, rp1.Y);
        Assert.NotEqual(host0.Y, rp1.Y);
    }

    [Fact]
    public void Layout_MiddlewareFromDifferentRoutes_DoNotShareARow()
    {
        // Two routes, each with one (different) middleware. The middleware must NOT line up on
        // the same row — that was the crossing-edges bug.
        var r0 = """{"handle":[{"handler":"authentication","providers":{"http_basic":{}}},{"handler":"reverse_proxy","upstreams":[{"dial":"127.0.0.1:1"}]}]}""";
        var r1 = """{"handle":[{"handler":"headers","response":{"set":{"X-A":["b"]}}},{"handler":"reverse_proxy","upstreams":[{"dial":"127.0.0.1:2"}]}]}""";
        var snap = new CaddySnapshot(
            CaddyStatus.Unknown,
            new[]
            {
                new Route("a.example", "127.0.0.1:1", true, "active", r0, "p0"),
                new Route("b.example", "127.0.0.1:2", true, "active", r1, "p1"),
            },
            System.Array.Empty<Cert>(), System.Array.Empty<Upstream>(),
            MetricsSnapshot.Unavailable, "{}", System.DateTimeOffset.Now);
        var placed = LayeredLayout.Arrange(TopologyGraph.Build(snap), 16, 3, 4, 1);

        var mids = placed.Where(p => p.Node.Kind == NodeKind.Middleware).ToList();
        Assert.Equal(2, mids.Count);
        Assert.NotEqual(mids[0].Y, mids[1].Y);  // on different swim-lanes → different rows
    }

    [Fact]
    public void Layout_EveryNonUpstreamNode_SharesItsLanesHostRow()
    {
        // Reproduces the screenshot bug: 4 routes with UNEVEN middleware counts. Under the old
        // order-within-layer layout, routes with fewer middleware leave column rows unfilled, so
        // a later route's proxy lands on an earlier route's row and edges cross between lanes.
        // Invariant: every host/middleware/proxy/terminal node sits on the same Y as its lane's host.
        var auth = """{"handler":"authentication","providers":{"http_basic":{}}}""";
        var hdr = """{"handler":"headers","response":{"set":{"X-A":["b"]}}}""";
        var enc = """{"handler":"encode","encodings":{"gzip":{}}}""";
        string Proxy(string d) => $$"""{"handler":"reverse_proxy","upstreams":[{"dial":"{{d}}"}]}""";
        string Route2(params string[] hs) => $$"""{"handle":[{{string.Join(",", hs)}}]}""";

        var routes = new[]
        {
            new Route("r0", "127.0.0.1:1", true, "active", Route2(auth, hdr, enc, Proxy("127.0.0.1:1")), "p0"), // 3 mw
            new Route("r1", "127.0.0.1:2", true, "active", Route2(Proxy("127.0.0.1:2")), "p1"),                  // 0 mw
            new Route("r2", "127.0.0.1:3", true, "active", Route2(hdr, Proxy("127.0.0.1:3")), "p2"),             // 1 mw
            new Route("r3", "",            false, "active", """{"handle":[{"handler":"file_server","root":"/x"}]}""", "p3"), // terminal
        };
        var snap = new CaddySnapshot(CaddyStatus.Unknown, routes,
            System.Array.Empty<Cert>(), System.Array.Empty<Upstream>(),
            MetricsSnapshot.Unavailable, "{}", System.DateTimeOffset.Now);
        var placed = LayeredLayout.Arrange(TopologyGraph.Build(snap), 16, 3, 4, 1);

        var hostYByLane = placed.Where(p => p.Node.Kind == NodeKind.Host)
                                .ToDictionary(p => p.Node.Lane, p => p.Y);

        foreach (var p in placed.Where(p => p.Node.Kind != NodeKind.Upstream))
            Assert.Equal(hostYByLane[p.Node.Lane], p.Y);  // every node shares its lane's host row
    }

    [Fact]
    public void Layout_AlignsLayersIntoColumns_AcrossLanes()
    {
        // The middleware column X for route 0 equals the middleware column X for any route —
        // i.e. layers still form vertical columns even though rows are per-route.
        var placed = LayeredLayout.Arrange(MixedChainGraph(), 16, 3, 4, 1);
        var host = placed.First(p => p.Node.Kind == NodeKind.Host);
        var mid = placed.First(p => p.Node.Kind == NodeKind.Middleware);
        var rp = placed.First(p => p.Node.Kind == NodeKind.ReverseProxy);
        Assert.True(mid.X > host.X);  // middleware column is right of host column
        Assert.True(rp.X > mid.X);    // proxy column is right of middleware column
    }

    [Fact]
    public void Layout_MultipleMiddlewareInOneChain_GetDistinctColumns()
    {
        // Regression: two middleware in the SAME route must land in different columns (different X),
        // not overlap in one box. (The bug rendered "authentication" + "headers" as "headersication".)
        var json = """
        {"handle":[
            {"handler":"authentication","providers":{"http_basic":{}}},
            {"handler":"headers","response":{"set":{"X-A":["b"]}}},
            {"handler":"reverse_proxy","upstreams":[{"dial":"127.0.0.1:1"}]}
        ]}
        """;
        var snap = new CaddySnapshot(CaddyStatus.Unknown,
            new[] { new Route("a.example", "127.0.0.1:1", true, "active", json, "p0") },
            System.Array.Empty<Cert>(), System.Array.Empty<Upstream>(),
            MetricsSnapshot.Unavailable, "{}", System.DateTimeOffset.Now);
        var placed = LayeredLayout.Arrange(TopologyGraph.Build(snap), 16, 3, 4, 1);

        var mids = placed.Where(p => p.Node.Kind == NodeKind.Middleware).ToList();
        Assert.Equal(2, mids.Count);
        Assert.NotEqual(mids[0].X, mids[1].X);  // distinct columns — no overlap
        Assert.Equal(mids[0].Y, mids[1].Y);     // same swim-lane row
    }
}
