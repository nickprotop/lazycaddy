using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class ConfigParserTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ParseRoutes_ExtractsHostUpstreamAndPath_FromRealConfig()
    {
        var routes = ConfigParser.ParseRoutes(Fixture("config.json"));

        var r = Assert.Single(routes);  // the real fixture has one route
        Assert.Equal("example.com", r.HostOrMatch);
        Assert.Equal("127.0.0.1:8090", r.Upstream);
        Assert.Equal("apps/http/servers/srv0/routes/0", r.ConfigPath);
        Assert.True(r.TlsEnabled); // host has a TLS automation policy
        Assert.Contains("reverse_proxy", r.RawConfigJson);
    }

    [Fact]
    public void ParseRoutes_ReturnsEmpty_WhenNoHttpApp()
    {
        var routes = ConfigParser.ParseRoutes("""{"apps":{}}""");
        Assert.Empty(routes);
    }

    [Fact]
    public void ParseCerts_ExtractsSubjectAndAcmeIssuer_FromRealConfig()
    {
        var certs = ConfigParser.ParseCerts(Fixture("config.json"));
        var c = Assert.Single(certs);
        Assert.Equal("example.com", c.Domain);
        Assert.Contains("ACME", c.Issuer, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal("managed", c.AcmeStatus);
    }

    [Fact]
    public void ParseCerts_ReturnsEmpty_WhenNoTlsApp()
        => Assert.Empty(ConfigParser.ParseCerts("""{"apps":{}}"""));

    // Two servers can serve the SAME host on different ports; the listen address is the only
    // thing that tells them apart, so routes and the server picker both have to carry it.
    private const string TwoServers = """
        {"apps":{"http":{"servers":{
          "srv0":{"listen":[":8443"],"routes":[{"match":[{"host":["a.example"]}],"handle":[]}]},
          "srv1":{"listen":[":8444"],"routes":[{"match":[{"host":["a.example"]}],"handle":[]}]}
        }}}}
        """;

    [Fact]
    public void ParseServers_ReturnsEachServerWithItsListen()
    {
        var servers = ConfigParser.ParseServers(TwoServers);
        Assert.Equal(2, servers.Count);
        Assert.Equal("srv0", servers[0].Name);
        Assert.Equal(":8443", servers[0].Listen);
        Assert.Equal("srv1 — :8444", servers[1].Label);
        Assert.Equal("apps/http/servers/srv1", servers[1].ConfigPath);
    }

    [Fact]
    public void ParseServers_ReturnsEmpty_WhenNoHttpApp()
        => Assert.Empty(ConfigParser.ParseServers("""{"apps":{}}"""));

    [Fact]
    public void ParseServers_LabelFallsBackToName_WhenListenAbsent()
    {
        var servers = ConfigParser.ParseServers("""{"apps":{"http":{"servers":{"srv0":{}}}}}""");
        var s = Assert.Single(servers);
        Assert.Equal("", s.Listen);
        Assert.Equal("srv0", s.Label);
    }

    [Fact]
    public void ParseRoutes_CarriesServerNameAndListen_SoDuplicateHostsAreDistinguishable()
    {
        var routes = ConfigParser.ParseRoutes(TwoServers);
        Assert.Equal(2, routes.Count);
        // Same host on both -- only ServerName/Listen separate them.
        Assert.Equal(routes[0].HostOrMatch, routes[1].HostOrMatch);
        Assert.Equal("srv0", routes[0].ServerName);
        Assert.Equal(":8443", routes[0].Listen);
        Assert.Equal("srv1", routes[1].ServerName);
        Assert.Equal(":8444", routes[1].Listen);
    }
}
