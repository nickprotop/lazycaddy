using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class AccessLogLocatorTests
{
    private const string FileConfig = """
        {"logging":{"logs":{"acc":{"writer":{"output":"file","filename":"/var/log/caddy/access.log"}}}},
         "apps":{"http":{"servers":{"srv0":{"logs":{"default_logger_name":"acc"}}}}}}
        """;

    private const string StderrConfig = """
        {"logging":{"logs":{"acc":{"writer":{"output":"stderr"}}}},
         "apps":{"http":{"servers":{"srv0":{"logs":{"default_logger_name":"acc"}}}}}}
        """;

    [Fact]
    public void Resolve_FileWriter_ReturnsFilePath()
    {
        var s = AccessLogLocator.Resolve(FileConfig, overridePath: null, urlIsLocal: true);
        Assert.Equal(LogSourceKind.File, s.Kind);
        Assert.Equal("/var/log/caddy/access.log", s.Path);
    }

    [Fact]
    public void Resolve_OverrideWins_EvenWhenRemote()
    {
        var s = AccessLogLocator.Resolve(StderrConfig, overridePath: "/tmp/my.log", urlIsLocal: false);
        Assert.Equal(LogSourceKind.File, s.Kind);
        Assert.Equal("/tmp/my.log", s.Path);
    }

    [Fact]
    public void Resolve_RemoteNoOverride_ReturnsRemote()
    {
        var s = AccessLogLocator.Resolve(FileConfig, overridePath: null, urlIsLocal: false);
        Assert.Equal(LogSourceKind.Remote, s.Kind);
    }

    [Fact]
    public void Resolve_StderrWriter_ReturnsNotConfigured()
    {
        var s = AccessLogLocator.Resolve(StderrConfig, overridePath: null, urlIsLocal: true);
        Assert.Equal(LogSourceKind.NotConfigured, s.Kind);
    }

    [Fact]
    public void Resolve_NoServerLogs_ReturnsNotConfigured()
    {
        var s = AccessLogLocator.Resolve("""{"apps":{"http":{"servers":{"srv0":{}}}}}""", null, true);
        Assert.Equal(LogSourceKind.NotConfigured, s.Kind);
    }

    [Fact]
    public void Resolve_MultiServer_PicksFirstFileWriter()
    {
        var cfg = """
            {"logging":{"logs":{"a":{"writer":{"output":"stderr"}},"b":{"writer":{"output":"file","filename":"/log/b.log"}}}},
             "apps":{"http":{"servers":{
                "srv0":{"logs":{"default_logger_name":"a"}},
                "srv1":{"logs":{"default_logger_name":"b"}}}}}}
            """;
        var s = AccessLogLocator.Resolve(cfg, null, true);
        Assert.Equal(LogSourceKind.File, s.Kind);
        Assert.Equal("/log/b.log", s.Path);
    }

    [Fact]
    public void UrlIsLocal_DetectsLocalhostAndLoopback()
    {
        Assert.True(AccessLogLocator.UrlIsLocal("http://localhost:2019"));
        Assert.True(AccessLogLocator.UrlIsLocal("http://127.0.0.1:2019"));
        Assert.True(AccessLogLocator.UrlIsLocal("http://[::1]:2019"));
        Assert.False(AccessLogLocator.UrlIsLocal("http://caddy.internal:2019"));
    }
}
