using System.Linq;
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

    // A route whose handle[] is a single reverse_proxy to the given dial.
    private static string ProxyJson(string dial) =>
        $$"""{"handle":[{"handler":"reverse_proxy","upstreams":[{"dial":"{{dial}}"}]}]}""";

    [Fact]
    public void Build_CreatesHostAndUpstreamNodes_AndAnEdgeBetween()
    {
        var g = TopologyGraph.Build(Snap(
            new Route("example.com", "127.0.0.1:8090", true, "active", ProxyJson("127.0.0.1:8090"), "p")));

        Assert.Contains(g.Nodes, n => n.Kind == NodeKind.Host && n.Label == "example.com");
        Assert.Contains(g.Nodes, n => n.Kind == NodeKind.Upstream && n.Label == "127.0.0.1:8090");
        Assert.NotEmpty(g.Edges);
    }

    [Fact]
    public void Build_MarksUpstreamHealth_FromSnapshot()
    {
        var g = TopologyGraph.Build(Snap(
            new Route("h", "127.0.0.1:8090", true, "active", ProxyJson("127.0.0.1:8090"), "p")));
        var up = Assert.Single(g.Nodes, n => n.Kind == NodeKind.Upstream);
        Assert.Equal(NodeHealth.Up, up.Health);
    }

    [Fact]
    public void Build_DedupesSharedUpstream_AcrossRoutes()
    {
        var g = TopologyGraph.Build(Snap(
            new Route("a", "127.0.0.1:8090", true, "active", ProxyJson("127.0.0.1:8090"), "p0"),
            new Route("b", "127.0.0.1:8090", true, "active", ProxyJson("127.0.0.1:8090"), "p1")));
        Assert.Single(g.Nodes, n => n.Kind == NodeKind.Upstream && n.Label == "127.0.0.1:8090");
        Assert.Equal(2, g.Nodes.Count(n => n.Kind == NodeKind.Host));
    }

    // ── Real handler chain ──────────────────────────────────────────────

    [Fact]
    public void Build_ChainsMiddlewareBeforeReverseProxy()
    {
        // handle: [authentication, headers, reverse_proxy] → Host → auth → headers → rp → upstream
        var json = """
        {"handle":[
            {"handler":"authentication","providers":{"http_basic":{}}},
            {"handler":"headers","response":{"set":{"X-Foo":["bar"]}}},
            {"handler":"reverse_proxy","upstreams":[{"dial":"127.0.0.1:8090"}]}
        ]}
        """;
        var g = TopologyGraph.Build(Snap(
            new Route("app.example.com", "127.0.0.1:8090", true, "active", json, "p")));

        var host = Assert.Single(g.Nodes, n => n.Kind == NodeKind.Host);
        var mids = g.Nodes.Where(n => n.Kind == NodeKind.Middleware).ToList();
        Assert.Equal(2, mids.Count);
        Assert.Contains(mids, m => m.Label == "authentication");
        Assert.Contains(mids, m => m.Label == "headers");
        var rp = Assert.Single(g.Nodes, n => n.Kind == NodeKind.ReverseProxy);
        var up = Assert.Single(g.Nodes, n => n.Kind == NodeKind.Upstream);

        var auth = mids.Single(m => m.Label == "authentication");
        var hdr = mids.Single(m => m.Label == "headers");
        // Edges form a chain Host → auth → headers → rp → up.
        Assert.Contains(g.Edges, e => e.FromId == host.Id && e.ToId == auth.Id);
        Assert.Contains(g.Edges, e => e.FromId == auth.Id && e.ToId == hdr.Id);
        Assert.Contains(g.Edges, e => e.FromId == hdr.Id && e.ToId == rp.Id);
        Assert.Contains(g.Edges, e => e.FromId == rp.Id && e.ToId == up.Id);
    }

    [Fact]
    public void Build_StaticResponseRoute_HasTerminalNode_NoPhantomProxy()
    {
        var json = """{"handle":[{"handler":"static_response","status_code":204}]}""";
        var g = TopologyGraph.Build(Snap(
            new Route("redir.example.com", "", false, "active", json, "p")));

        Assert.DoesNotContain(g.Nodes, n => n.Kind == NodeKind.ReverseProxy);
        Assert.DoesNotContain(g.Nodes, n => n.Kind == NodeKind.Upstream);
        var term = Assert.Single(g.Nodes, n => n.Kind == NodeKind.Terminal);
        Assert.Equal("static_response", term.Label);
        var host = Assert.Single(g.Nodes, n => n.Kind == NodeKind.Host);
        Assert.Contains(g.Edges, e => e.FromId == host.Id && e.ToId == term.Id);
    }

    [Fact]
    public void Build_FileServerRoute_HasTerminalNode()
    {
        var json = """{"handle":[{"handler":"file_server","root":"/srv"}]}""";
        var g = TopologyGraph.Build(Snap(
            new Route("static.example.com", "", false, "active", json, "p")));

        var term = Assert.Single(g.Nodes, n => n.Kind == NodeKind.Terminal);
        Assert.Equal("file_server", term.Label);
        Assert.DoesNotContain(g.Nodes, n => n.Kind == NodeKind.ReverseProxy);
    }

    [Fact]
    public void Build_SubrouteWrappedReverseProxy_ResolvesThroughSubroute()
    {
        // ParseHandlers recurses subroutes; the reverse_proxy inside should still produce
        // a ReverseProxy node + upstream. The subroute container itself is not a node.
        var json = """
        {"handle":[{"handler":"subroute","routes":[
            {"handle":[{"handler":"reverse_proxy","upstreams":[{"dial":"127.0.0.1:8090"}]}]}
        ]}]}
        """;
        var g = TopologyGraph.Build(Snap(
            new Route("wrapped.example.com", "127.0.0.1:8090", true, "active", json, "p")));

        Assert.Single(g.Nodes, n => n.Kind == NodeKind.ReverseProxy);
        Assert.Single(g.Nodes, n => n.Kind == NodeKind.Upstream && n.Label == "127.0.0.1:8090");
        // No node represents the subroute container.
        Assert.DoesNotContain(g.Nodes, n => n.Label == "subroute");
    }

    [Fact]
    public void Build_RouteWithNoHandlers_FallsBackToProxyFromUpstreamString()
    {
        // Empty handle (e.g. dummy data with only an Upstream string) still shows a proxy
        // chain so the topology isn't blank — Host → reverse_proxy → upstream.
        var g = TopologyGraph.Build(Snap(
            new Route("legacy.example.com", "127.0.0.1:8090", true, "active", "{}", "p")));

        Assert.Single(g.Nodes, n => n.Kind == NodeKind.ReverseProxy);
        Assert.Single(g.Nodes, n => n.Kind == NodeKind.Upstream && n.Label == "127.0.0.1:8090");
    }

    [Fact]
    public void Build_RouteWithNoHandlersAndNoUpstream_IsJustHost()
    {
        var g = TopologyGraph.Build(Snap(
            new Route("bare.example.com", "", false, "active", "{}", "p")));

        Assert.Single(g.Nodes, n => n.Kind == NodeKind.Host);
        Assert.Single(g.Nodes); // nothing but the host
        Assert.Empty(g.Edges);
    }

    [Fact]
    public void Build_MiddlewareNodesHaveUnknownHealth()
    {
        var json = """
        {"handle":[
            {"handler":"encode","encodings":{"gzip":{}}},
            {"handler":"reverse_proxy","upstreams":[{"dial":"127.0.0.1:8090"}]}
        ]}
        """;
        var g = TopologyGraph.Build(Snap(
            new Route("h", "127.0.0.1:8090", true, "active", json, "p")));
        var mid = Assert.Single(g.Nodes, n => n.Kind == NodeKind.Middleware);
        Assert.Equal(NodeHealth.Unknown, mid.Health);
    }
}
