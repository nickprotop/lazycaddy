using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using Xunit;

namespace LazyCaddy.Tests;

public class ConnectionContextTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lc-ctx-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_BuildsConfigFromEntry_WithOverridesAndScopedSnapshots()
    {
        var entry = new ServerEntry("prod", "http://localhost:2019") { ReadOnly = true, CertDir = "/data" };
        var ctx = ConnectionContext.Create(entry, snapshotRoot: _dir, generation: 7);

        Assert.Equal("http://localhost:2019", ctx.Config.AdminApiUrl);
        Assert.True(ctx.Config.ReadOnly);
        Assert.Equal("/data", ctx.Config.CaddyDataDir);
        Assert.Equal(7, ctx.Generation);
        Assert.NotNull(ctx.Admin);
        Assert.NotNull(ctx.Editor);
        Assert.StartsWith(_dir, ctx.Config.InstanceSnapshotDir);
    }

    public void Dispose() { try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { } }
}
