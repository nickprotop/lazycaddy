using System.Text.Json;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class EditPatchBuilderTests
{
    [Fact]
    public void UpstreamsArray_BuildsDialList()
    {
        var json = EditPatchBuilder.UpstreamsArray(new[] { "127.0.0.1:8090", "127.0.0.1:9000" });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("127.0.0.1:8090", doc.RootElement[0].GetProperty("dial").GetString());
    }

    [Fact]
    public void HostMatcher_BuildsMatchArray()
    {
        var json = EditPatchBuilder.HostMatcher(new[] { "a.example.com", "b.example.com" });
        using var doc = JsonDocument.Parse(json);
        var hosts = doc.RootElement[0].GetProperty("host");
        Assert.Equal(2, hosts.GetArrayLength());
    }

    [Fact]
    public void ReverseProxyRoute_BuildsCompleteRoute()
    {
        var json = EditPatchBuilder.ReverseProxyRoute("site.example.com", "127.0.0.1:3000");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("site.example.com",
            doc.RootElement.GetProperty("match")[0].GetProperty("host")[0].GetString());
        Assert.Equal("reverse_proxy",
            doc.RootElement.GetProperty("handle")[0].GetProperty("handler").GetString());
    }
}
