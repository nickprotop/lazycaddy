using LazyCaddy.Configuration;
using Xunit;

namespace LazyCaddy.Tests;

public class InstanceSlugTests
{
    [Fact]
    public void Slug_LocalDefault_HumanReadableHostPort()
    {
        var slug = LazyCaddyConfig.InstanceSlug("http://localhost:2019");
        Assert.StartsWith("localhost_2019-", slug);
    }

    [Fact]
    public void Slug_HttpAndHttps_SameHostPort_AreEqual()
    {
        // The admin API identity is host:port; scheme must not split snapshots.
        Assert.Equal(
            LazyCaddyConfig.InstanceSlug("http://caddy.internal:2019"),
            LazyCaddyConfig.InstanceSlug("https://caddy.internal:2019"));
    }

    [Fact]
    public void Slug_DifferentHosts_Differ()
    {
        Assert.NotEqual(
            LazyCaddyConfig.InstanceSlug("http://host-a:2019"),
            LazyCaddyConfig.InstanceSlug("http://host-b:2019"));
    }

    [Fact]
    public void Slug_DifferentPorts_SameHost_Differ()
    {
        Assert.NotEqual(
            LazyCaddyConfig.InstanceSlug("http://localhost:2019"),
            LazyCaddyConfig.InstanceSlug("http://localhost:2020"));
    }

    [Fact]
    public void Slug_HostCaseInsensitive()
    {
        Assert.Equal(
            LazyCaddyConfig.InstanceSlug("http://Caddy.Internal:2019"),
            LazyCaddyConfig.InstanceSlug("http://caddy.internal:2019"));
    }

    [Fact]
    public void Slug_TrailingSlashIgnored()
    {
        Assert.Equal(
            LazyCaddyConfig.InstanceSlug("http://localhost:2019/"),
            LazyCaddyConfig.InstanceSlug("http://localhost:2019"));
    }

    [Fact]
    public void Slug_DefaultPortFilledIn_HttpVsExplicit80Equal()
    {
        Assert.Equal(
            LazyCaddyConfig.InstanceSlug("http://example.com"),
            LazyCaddyConfig.InstanceSlug("http://example.com:80"));
    }

    [Fact]
    public void Slug_OnlySafeFilesystemChars()
    {
        var slug = LazyCaddyConfig.InstanceSlug("http://10.0.0.5:2019");
        Assert.Matches("^[a-z0-9._-]+$", slug);
    }

    [Fact]
    public void Slug_UnparseableUrl_DoesNotThrow_AndIsSafe()
    {
        var slug = LazyCaddyConfig.InstanceSlug("not a url at all");
        Assert.False(string.IsNullOrWhiteSpace(slug));
        Assert.Matches("^[a-z0-9._-]+$", slug);
    }

    [Fact]
    public void InstanceSnapshotDir_IsBaseDirPlusSlug()
    {
        var cfg = LazyCaddyConfig.Default with { AdminApiUrl = "http://localhost:2019" };
        var slug = LazyCaddyConfig.InstanceSlug(cfg.AdminApiUrl);
        Assert.Equal(Path.Combine(cfg.SnapshotDir, slug), cfg.InstanceSnapshotDir);
    }
}
