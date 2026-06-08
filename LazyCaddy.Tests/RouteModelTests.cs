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
}
