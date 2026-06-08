using System.Text.Json;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class HandlerPatchH3Tests
{
    [Fact]
    public void Templates_BuildsFileRootAndMime()
    {
        var json = HandlerPatch.Templates(fileRoot: "/srv", mimeTypes: new[] { "text/html" });
        using var d = JsonDocument.Parse(json);
        Assert.Equal("templates", d.RootElement.GetProperty("handler").GetString());
        Assert.Equal("/srv", d.RootElement.GetProperty("file_root").GetString());
        Assert.Equal("text/html", d.RootElement.GetProperty("mime_types")[0].GetString());
    }

    [Fact]
    public void Templates_OmitsEmpty()
    {
        var json = HandlerPatch.Templates("", System.Array.Empty<string>());
        using var d = JsonDocument.Parse(json);
        Assert.Equal("templates", d.RootElement.GetProperty("handler").GetString());
        Assert.False(d.RootElement.TryGetProperty("file_root", out _));
        Assert.False(d.RootElement.TryGetProperty("mime_types", out _));
    }

    [Fact]
    public void ReverseProxy_BuildsUpstreamsAndFlush()
    {
        var json = HandlerPatch.ReverseProxy(new[] { "127.0.0.1:8090", "127.0.0.1:9000" }, flushInterval: -1);
        using var d = JsonDocument.Parse(json);
        Assert.Equal("reverse_proxy", d.RootElement.GetProperty("handler").GetString());
        Assert.Equal("127.0.0.1:8090", d.RootElement.GetProperty("upstreams")[0].GetProperty("dial").GetString());
        Assert.Equal(-1, d.RootElement.GetProperty("flush_interval").GetInt32());
    }

    [Fact]
    public void ReverseProxy_OmitsFlushWhenZero()
    {
        var json = HandlerPatch.ReverseProxy(new[] { "x:1" }, flushInterval: 0);
        using var d = JsonDocument.Parse(json);
        Assert.False(d.RootElement.TryGetProperty("flush_interval", out _));
    }
}
