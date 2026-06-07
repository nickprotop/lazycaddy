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
}
