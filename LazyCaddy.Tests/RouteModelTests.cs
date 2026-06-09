using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class RouteModelTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parse_FlatFileServer_OneDescriptor_WithPathAndType()
    {
        var hs = RouteModel.ParseHandlers(Fixture("route_fileserver.json"), "apps/http/servers/srv0/routes/0");
        var h = Assert.Single(hs);
        Assert.Equal("file_server", h.Type);
        Assert.Equal("apps/http/servers/srv0/routes/0/handle/0", h.ConfigPath);
        Assert.Equal(0, h.Depth);
    }

    [Fact]
    public void Parse_Subroute_RecursesIntoNestedReverseProxy()
    {
        var hs = RouteModel.ParseHandlers(Fixture("route_subroute_rp.json"), "apps/http/servers/srv0/routes/0");
        Assert.Contains(hs, d => d.Type == "subroute" && d.ConfigPath == "apps/http/servers/srv0/routes/0/handle/0");
        Assert.Contains(hs, d => d.Type == "reverse_proxy" &&
            d.ConfigPath == "apps/http/servers/srv0/routes/0/handle/0/routes/0/handle/0" && d.Depth == 1);
    }

    [Fact]
    public void Parse_NoHandle_ReturnsEmpty()
        => Assert.Empty(RouteModel.ParseHandlers("""{"match":[]}""", "p"));

    // --- Richer handler summaries ------------------------------------------------

    // Parse a single-handler route from inline JSON and return that handler's Summary.
    private static string SummaryOf(string handlerJson)
    {
        var route = $$"""{"handle":[{{handlerJson}}]}""";
        var hs = RouteModel.ParseHandlers(route, "p");
        return Assert.Single(hs).Summary;
    }

    [Fact]
    public void Summary_ReverseProxy_OneOrTwoUpstreams_ListsDials()
    {
        Assert.Equal("→ 127.0.0.1:9001",
            SummaryOf("""{"handler":"reverse_proxy","upstreams":[{"dial":"127.0.0.1:9001"}]}"""));
        Assert.Equal("→ 127.0.0.1:9001, 127.0.0.1:9002",
            SummaryOf("""{"handler":"reverse_proxy","upstreams":[{"dial":"127.0.0.1:9001"},{"dial":"127.0.0.1:9002"}]}"""));
    }

    [Fact]
    public void Summary_ReverseProxy_ManyUpstreams_Abbreviates()
        => Assert.Equal("→ a:1 +2 more",
            SummaryOf("""{"handler":"reverse_proxy","upstreams":[{"dial":"a:1"},{"dial":"b:2"},{"dial":"c:3"}]}"""));

    [Fact]
    public void Summary_ReverseProxy_NoUpstreams()
        => Assert.Equal("reverse_proxy (no upstreams)",
            SummaryOf("""{"handler":"reverse_proxy","upstreams":[]}"""));

    [Fact]
    public void Summary_StaticResponse_WithBody()
        => Assert.Equal("respond 200 \"hello\"",
            SummaryOf("""{"handler":"static_response","status_code":200,"body":"hello"}"""));

    [Fact]
    public void Summary_StaticResponse_LocationIsRedirect()
        => Assert.Equal("redirect 302 → https://example.com",
            SummaryOf("""{"handler":"static_response","status_code":302,"headers":{"Location":["https://example.com"]}}"""));

    [Fact]
    public void Summary_Rewrite_Uri()
        => Assert.Equal("uri → /new", SummaryOf("""{"handler":"rewrite","uri":"/new"}"""));

    [Fact]
    public void Summary_Headers_CountsOps()
        => Assert.Equal("headers resp +2 -1",
            SummaryOf("""{"handler":"headers","response":{"set":{"X-A":["1"],"X-B":["2"]},"delete":["X-C"]}}"""));

    [Fact]
    public void Summary_Encode_ListsEncodings()
        => Assert.Equal("encode gzip/zstd",
            SummaryOf("""{"handler":"encode","encodings":{"gzip":{},"zstd":{}}}"""));

    [Fact]
    public void Summary_Authentication_ListsProviders()
        => Assert.Equal("auth http_basic",
            SummaryOf("""{"handler":"authentication","providers":{"http_basic":{}}}"""));

    [Fact]
    public void Summary_FileServer_Root()
        => Assert.Equal("root /var/www", SummaryOf("""{"handler":"file_server","root":"/var/www"}"""));

    [Fact]
    public void Summary_Templates_HasFriendlyText()
        => Assert.Equal("render templates", SummaryOf("""{"handler":"templates"}"""));

    [Fact]
    public void Summary_UnknownType_FallsBackToType()
        => Assert.Equal("some_custom", SummaryOf("""{"handler":"some_custom"}"""));
}
