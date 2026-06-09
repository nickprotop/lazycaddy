using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class AdaptParserTests
{
    [Fact]
    public void Parse_Success_ExtractsResultAndWarnings()
    {
        var body = """
            {"warnings":[{"file":"Caddyfile","line":3,"message":"not formatted"}],
             "result":{"apps":{"http":{"servers":{"srv0":{"listen":[":443"]}}}}}}
            """;
        var r = AdaptParser.Parse(httpOk: true, body);
        Assert.True(r.Success);
        Assert.Null(r.Error);
        Assert.Contains("\"apps\"", r.ResultJson);
        Assert.Contains("srv0", r.ResultJson);
        var w = Assert.Single(r.Warnings);
        Assert.Equal("Caddyfile", w.File);
        Assert.Equal(3, w.Line);
        Assert.Equal("not formatted", w.Message);
    }

    [Fact]
    public void Parse_Success_PrettyPrintsResult()
    {
        var r = AdaptParser.Parse(true, """{"result":{"a":1}}""");
        Assert.True(r.Success);
        Assert.Contains("\n", r.ResultJson); // indented => multi-line
    }

    [Fact]
    public void Parse_Error_SurfacesMessage()
    {
        var r = AdaptParser.Parse(httpOk: false, """{"error":"syntax error: unexpected token"}""");
        Assert.False(r.Success);
        Assert.Equal("syntax error: unexpected token", r.Error);
        Assert.Null(r.ResultJson);
    }

    [Fact]
    public void Parse_ErrorBody_EvenWhenHttpOk()
    {
        // Defensive: an {"error"} body wins regardless of the HTTP flag.
        var r = AdaptParser.Parse(httpOk: true, """{"error":"boom"}""");
        Assert.False(r.Success);
        Assert.Equal("boom", r.Error);
    }

    [Fact]
    public void Parse_EmptyBody_Fails()
    {
        Assert.False(AdaptParser.Parse(true, "").Success);
        Assert.False(AdaptParser.Parse(false, "   ").Success);
    }

    [Fact]
    public void Parse_NoWarnings_EmptyList()
    {
        var r = AdaptParser.Parse(true, """{"result":{"x":1}}""");
        Assert.True(r.Success);
        Assert.Empty(r.Warnings);
    }
}
