using System.Text.Json;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class HandlerPatchH2Tests
{
    [Fact]
    public void Rewrite_BuildsMethodUriStrip()
    {
        var json = HandlerPatch.Rewrite(method: "GET", uri: "/new", stripPrefix: "/old", stripSuffix: "");
        using var d = JsonDocument.Parse(json);
        Assert.Equal("rewrite", d.RootElement.GetProperty("handler").GetString());
        Assert.Equal("GET", d.RootElement.GetProperty("method").GetString());
        Assert.Equal("/new", d.RootElement.GetProperty("uri").GetString());
        Assert.Equal("/old", d.RootElement.GetProperty("strip_path_prefix").GetString());
        Assert.False(d.RootElement.TryGetProperty("strip_path_suffix", out _)); // empty omitted
    }

    [Fact]
    public void Headers_BuildsRequestAndResponseOps()
    {
        var req = new HeaderOpsInput(
            Set: new[] { ("X-A", "1") }, Add: System.Array.Empty<(string, string)>(), Delete: new[] { "X-Old" });
        var resp = new HeaderOpsInput(
            Add: new[] { ("X-B", "2") }, Set: System.Array.Empty<(string, string)>(), Delete: System.Array.Empty<string>());
        var json = HandlerPatch.Headers(req, resp);
        using var d = JsonDocument.Parse(json);
        Assert.Equal("headers", d.RootElement.GetProperty("handler").GetString());
        Assert.Equal("1", d.RootElement.GetProperty("request").GetProperty("set").GetProperty("X-A")[0].GetString());
        Assert.Equal("X-Old", d.RootElement.GetProperty("request").GetProperty("delete")[0].GetString());
        Assert.Equal("2", d.RootElement.GetProperty("response").GetProperty("add").GetProperty("X-B")[0].GetString());
    }

    [Fact]
    public void Headers_OmitsEmptyOps()
    {
        var empty = new HeaderOpsInput(System.Array.Empty<(string, string)>(), System.Array.Empty<(string, string)>(), System.Array.Empty<string>());
        var json = HandlerPatch.Headers(empty, empty);
        using var d = JsonDocument.Parse(json);
        Assert.False(d.RootElement.TryGetProperty("request", out _));
        Assert.False(d.RootElement.TryGetProperty("response", out _));
    }

    [Fact]
    public void Encode_BuildsEncodingsAndMinLength()
    {
        var json = HandlerPatch.Encode(gzip: true, zstd: true, minimumLength: 256);
        using var d = JsonDocument.Parse(json);
        Assert.Equal("encode", d.RootElement.GetProperty("handler").GetString());
        Assert.True(d.RootElement.GetProperty("encodings").TryGetProperty("gzip", out _));
        Assert.True(d.RootElement.GetProperty("encodings").TryGetProperty("zstd", out _));
        Assert.Equal(256, d.RootElement.GetProperty("minimum_length").GetInt32());
    }

    [Fact]
    public void Vars_BuildsKeyValueMap()
    {
        var json = HandlerPatch.Vars(new[] { ("root", "/srv"), ("env", "prod") });
        using var d = JsonDocument.Parse(json);
        Assert.Equal("vars", d.RootElement.GetProperty("handler").GetString());
        Assert.Equal("/srv", d.RootElement.GetProperty("root").GetString());
        Assert.Equal("prod", d.RootElement.GetProperty("env").GetString());
    }

    [Fact]
    public void RequestBody_BuildsMaxSize()
    {
        var json = HandlerPatch.RequestBody(maxSize: 1048576);
        using var d = JsonDocument.Parse(json);
        Assert.Equal("request_body", d.RootElement.GetProperty("handler").GetString());
        Assert.Equal(1048576, d.RootElement.GetProperty("max_size").GetInt64());
    }
}
