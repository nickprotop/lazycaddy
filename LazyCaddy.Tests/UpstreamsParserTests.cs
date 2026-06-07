using LazyCaddy.Models;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class UpstreamsParserTests
{
    [Fact]
    public void Parse_ReadsAddresses_AndDefaultsReachabilityUnknown()
    {
        var json = """[{"address":"127.0.0.1:8090","num_requests":0,"fails":0}]""";
        var list = UpstreamsParser.Parse(json);
        var u = Assert.Single(list);
        Assert.Equal("127.0.0.1:8090", u.Address);
        Assert.Equal(UpstreamReachability.Unknown, u.Reachability);
        Assert.Empty(u.UsedByRoutes);
    }

    [Fact]
    public void Parse_ReturnsEmpty_OnEmptyArray()
        => Assert.Empty(UpstreamsParser.Parse("[]"));
}
