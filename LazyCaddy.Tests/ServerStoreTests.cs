using LazyCaddy.Configuration;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class ServerStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lc-srv-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "servers.json");

    [Fact]
    public void Load_MissingFile_ReturnsImplicitLocalDefault()
    {
        var store = new ServerStore(FilePath);
        var result = store.Load();
        Assert.False(result.Malformed);
        var entry = Assert.Single(result.Servers);
        Assert.Equal("local", entry.Name);
        Assert.Equal("http://localhost:2019", entry.Url);
    }

    [Fact]
    public void Load_ValidFile_ReturnsEntries()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
        {"servers":[{"name":"prod","url":"http://localhost:2019"},
                    {"name":"edge","url":"https://edge:2019","readOnly":true}]}
        """);
        var result = new ServerStore(FilePath).Load();
        Assert.False(result.Malformed);
        Assert.Equal(new[] { "prod", "edge" }, result.Servers.Select(s => s.Name).ToArray());
        Assert.True(result.Servers[1].ReadOnly);
    }

    [Fact]
    public void Load_MalformedFile_FlagsMalformed_AndFallsBackToDefault()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json");
        var result = new ServerStore(FilePath).Load();
        Assert.True(result.Malformed);
        Assert.Single(result.Servers);
    }

    [Fact]
    public void Load_DuplicateIdentities_KeepsFirst_FlagsWarning()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
        {"servers":[{"name":"a","url":"http://h:2019"},{"name":"b","url":"https://h:2019"}]}
        """);
        var result = new ServerStore(FilePath).Load();
        var entry = Assert.Single(result.Servers);
        Assert.Equal("a", entry.Name);
        Assert.True(result.HadDuplicates);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips_AndOmitsEphemeral()
    {
        var store = new ServerStore(FilePath);
        var list = new[]
        {
            new ServerEntry("prod", "http://localhost:2019"),
            new ServerEntry("(cli)", "http://x:2019") { IsEphemeral = true },
        };
        store.Save(list);
        var loaded = store.Load();
        var entry = Assert.Single(loaded.Servers);   // ephemeral not written
        Assert.Equal("prod", entry.Name);
    }

    [Fact]
    public void Validate_RejectsBadUrlAndDuplicateName()
    {
        Assert.NotNull(ServerStore.Validate(new ServerEntry("", "http://h:2019"), Array.Empty<ServerEntry>()));   // empty name
        Assert.NotNull(ServerStore.Validate(new ServerEntry("x", "not a url"), Array.Empty<ServerEntry>()));      // bad url
        var existing = new[] { new ServerEntry("dup", "http://h:2019") };
        Assert.NotNull(ServerStore.Validate(new ServerEntry("dup", "http://h2:2019"), existing));                 // dup name
        Assert.Null(ServerStore.Validate(new ServerEntry("ok", "http://h2:2019"), existing));                     // valid
    }

    public void Dispose() { try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { } }
}
