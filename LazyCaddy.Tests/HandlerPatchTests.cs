using System.Text.Json;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class HandlerPatchTests
{
    [Fact]
    public void FileServer_BuildsRootIndexHide()
    {
        var json = HandlerPatch.FileServer(root: "/var/www", indexNames: new[] { "index.html" },
            hide: new[] { ".git" }, browse: false, passThru: false);
        using var d = JsonDocument.Parse(json);
        Assert.Equal("file_server", d.RootElement.GetProperty("handler").GetString());
        Assert.Equal("/var/www", d.RootElement.GetProperty("root").GetString());
        Assert.Equal("index.html", d.RootElement.GetProperty("index_names")[0].GetString());
        Assert.False(d.RootElement.TryGetProperty("browse", out _));
    }

    [Fact]
    public void FileServer_BrowseOn_AddsEmptyBrowseObject()
    {
        var json = HandlerPatch.FileServer("/srv", System.Array.Empty<string>(), System.Array.Empty<string>(), browse: true, passThru: true);
        using var d = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, d.RootElement.GetProperty("browse").ValueKind);
        Assert.True(d.RootElement.GetProperty("pass_thru").GetBoolean());
    }

    [Fact]
    public void StaticResponse_BuildsStatusAndBody()
    {
        var json = HandlerPatch.StaticResponse(statusCode: 200, body: "hi", close: false);
        using var d = JsonDocument.Parse(json);
        Assert.Equal("static_response", d.RootElement.GetProperty("handler").GetString());
        Assert.Equal(200, d.RootElement.GetProperty("status_code").GetInt32());
        Assert.Equal("hi", d.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void Error_BuildsMessageAndStatus()
    {
        var json = HandlerPatch.Error(message: "nope", statusCode: 403);
        using var d = JsonDocument.Parse(json);
        Assert.Equal("error", d.RootElement.GetProperty("handler").GetString());
        Assert.Equal(403, d.RootElement.GetProperty("status_code").GetInt32());
        Assert.Equal("nope", d.RootElement.GetProperty("error").GetString());
    }
}
