using LazyCaddy.Configuration;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class ServerSelectionTests
{
    private static readonly IReadOnlyList<ServerEntry> Configured = new[]
    {
        new ServerEntry("prod", "http://localhost:2019"),
        new ServerEntry("edge", "https://edge:2019"),
    };

    [Fact]
    public void NoUrl_SelectsFirstConfigured()
    {
        var r = ServerSelection.Resolve(cliUrl: null, Configured);
        Assert.Equal(2, r.Servers.Count);
        Assert.Equal(0, r.ActiveIndex);
    }

    [Fact]
    public void Url_MatchingConfigured_SelectsThatEntry_NoEphemeral()
    {
        var r = ServerSelection.Resolve(cliUrl: "https://edge:2019", Configured);
        Assert.Equal(2, r.Servers.Count);
        Assert.Equal("edge", r.Servers[r.ActiveIndex].Name);
        Assert.DoesNotContain(r.Servers, s => s.IsEphemeral);
    }

    [Fact]
    public void Url_Unmatched_AddsEphemeral_AndSelectsIt()
    {
        var r = ServerSelection.Resolve(cliUrl: "http://other:2019", Configured);
        Assert.Equal(3, r.Servers.Count);
        var active = r.Servers[r.ActiveIndex];
        Assert.True(active.IsEphemeral);
        Assert.Equal("http://other:2019", active.Url);
    }
}
