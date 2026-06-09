using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class AccessLogParserTests
{
    [Fact]
    public void Parse_RealCaddyLine_ExtractsFields()
    {
        var line = """
            {"level":"info","ts":1672531200.5,"logger":"http.log.access.log0","msg":"handled request","request":{"remote_ip":"1.2.3.4","proto":"HTTP/2.0","method":"GET","host":"api.test","uri":"/v1/users"},"duration":0.0125,"size":1234,"status":200}
            """;
        var e = AccessLogParser.Parse(line);
        Assert.NotNull(e);
        Assert.Equal(200, e!.Status);
        Assert.Equal("GET", e.Method);
        Assert.Equal("api.test", e.Host);
        Assert.Equal("/v1/users", e.Uri);
        Assert.Equal(0.0125, e.DurationSeconds, 5);
        Assert.Equal(1234, e.Size);
        Assert.False(e.IsRaw);
        Assert.Equal(2023, e.Time.UtcDateTime.Year);
    }

    [Fact]
    public void Parse_NonJson_ReturnsRawEntry()
    {
        var e = AccessLogParser.Parse("not json at all");
        Assert.NotNull(e);
        Assert.True(e!.IsRaw);
        Assert.Equal("not json at all", e.Raw);
    }

    [Fact]
    public void Parse_BlankLine_ReturnsNull()
    {
        Assert.Null(AccessLogParser.Parse("   "));
        Assert.Null(AccessLogParser.Parse(""));
    }

    [Fact]
    public void Parse_MissingFields_Tolerated()
    {
        var e = AccessLogParser.Parse("""{"level":"info","msg":"something else"}""");
        Assert.NotNull(e);
        Assert.True(e!.IsRaw);
    }

    [Fact]
    public void Parse_PartialRequest_FillsWhatItCan()
    {
        var line = """{"ts":1672531200,"status":404,"request":{"method":"POST","host":"x","uri":"/y"},"duration":0.5}""";
        var e = AccessLogParser.Parse(line);
        Assert.NotNull(e);
        Assert.Equal(404, e!.Status);
        Assert.Equal("POST", e.Method);
        Assert.Equal(0, e.Size);
        Assert.False(e.IsRaw);
    }
}
