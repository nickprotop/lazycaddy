using LazyCaddy.Configuration;
using Xunit;

namespace LazyCaddy.Tests;

public class ServerEntryTests
{
    [Fact]
    public void Identity_SameHostPort_DifferentScheme_AreEqual()
    {
        var a = new ServerEntry("a", "http://h:2019");
        var b = new ServerEntry("b", "https://h:2019");
        Assert.Equal(a.Identity, b.Identity);
    }

    [Fact]
    public void Identity_DifferentPort_Differs()
    {
        var a = new ServerEntry("a", "http://h:2019");
        var b = new ServerEntry("b", "http://h:2020");
        Assert.NotEqual(a.Identity, b.Identity);
    }

    [Fact]
    public void Ephemeral_DefaultsFalse_NotSerializedFlagSettable()
    {
        var e = new ServerEntry("(cli)", "http://h:2019") { IsEphemeral = true };
        Assert.True(e.IsEphemeral);
        Assert.False(new ServerEntry("p", "http://h:2019").IsEphemeral);
    }
}
