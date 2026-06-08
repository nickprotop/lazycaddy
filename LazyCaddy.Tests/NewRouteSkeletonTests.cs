using System.Text.Json;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class NewRouteSkeletonTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("file_server")]
    [InlineData("error")]
    [InlineData("rewrite")]
    [InlineData("headers")]
    [InlineData("encode")]
    [InlineData("vars")]
    [InlineData("request_body")]
    [InlineData("templates")]
    [InlineData("authentication")]
    [InlineData("static_response")]
    public void MinimalHandler_PlainTypes_EmitOnlyHandlerKey(string type)
    {
        var r = Parse(NewRouteSkeleton.MinimalHandler(type));
        Assert.Equal(type, r.GetProperty("handler").GetString());
    }

    [Fact]
    public void MinimalHandler_ReverseProxy_HasEmptyUpstreams()
    {
        var r = Parse(NewRouteSkeleton.MinimalHandler("reverse_proxy"));
        Assert.Equal("reverse_proxy", r.GetProperty("handler").GetString());
        Assert.Equal(JsonValueKind.Array, r.GetProperty("upstreams").ValueKind);
        Assert.Equal(0, r.GetProperty("upstreams").GetArrayLength());
    }

    [Fact]
    public void MinimalHandler_Redir_IsStaticResponseWithLocationAnd302()
    {
        var r = Parse(NewRouteSkeleton.MinimalHandler("redir"));
        Assert.Equal("static_response", r.GetProperty("handler").GetString());
        Assert.Equal(302, r.GetProperty("status_code").GetInt32());
        Assert.True(r.GetProperty("headers").TryGetProperty("Location", out var loc));
        Assert.Equal(JsonValueKind.Array, loc.ValueKind);
    }

    [Fact]
    public void MinimalHandler_UnknownType_FallsBackToHandlerKey()
    {
        var r = Parse(NewRouteSkeleton.MinimalHandler("something_custom"));
        Assert.Equal("something_custom", r.GetProperty("handler").GetString());
    }

    [Fact]
    public void OfferedTypes_IncludesLeafAndMiddlewareFormsPlusRedir_ExcludesSubroute()
    {
        var types = NewRouteSkeleton.OfferedTypes.Select(t => t.Type).ToList();
        foreach (var t in new[] { "reverse_proxy", "file_server", "static_response", "error",
            "rewrite", "headers", "encode", "vars", "request_body", "templates", "authentication", "redir" })
            Assert.Contains(t, types);
        Assert.DoesNotContain("subroute", types);
    }

    [Fact]
    public void FormType_Redir_MapsToStaticResponse()
    {
        Assert.Equal("static_response", NewRouteSkeleton.FormType("redir"));
        Assert.Equal("file_server", NewRouteSkeleton.FormType("file_server"));
    }
}
