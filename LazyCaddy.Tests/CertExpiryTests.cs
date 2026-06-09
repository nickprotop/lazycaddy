using LazyCaddy.Models;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class CertExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

    private static Cert CertExpiringInDays(string domain, double days)
        => new(domain, "Let's Encrypt", Now.AddDays(days), "managed");

    [Fact]
    public void Summarize_BucketsByThreshold()
    {
        var certs = new[]
        {
            CertExpiringInDays("ok.example", 90),       // Ok
            CertExpiringInDays("warn.example", 20),     // Warning (14..<30)
            CertExpiringInDays("crit.example", 5),      // Critical (0..<14)
            CertExpiringInDays("expired.example", -3),  // Expired
        };
        var h = CertExpiry.Summarize(certs, Now);
        Assert.Equal(4, h.Total);
        Assert.Equal(1, h.Expired);
        Assert.Equal(1, h.Critical);
        Assert.Equal(1, h.Warning);
        Assert.Equal(1, h.Ok);
        Assert.True(h.HasAlert);
        Assert.True(h.HasCritical);
    }

    [Fact]
    public void Summarize_BoundariesAreInclusiveLow()
    {
        // Exactly 14 days → Warning (not Critical); exactly 30 → Ok (not Warning).
        var certs = new[]
        {
            CertExpiringInDays("at14.example", 14),
            CertExpiringInDays("at30.example", 30),
        };
        var h = CertExpiry.Summarize(certs, Now);
        Assert.Equal(0, h.Critical);
        Assert.Equal(1, h.Warning); // the 14-day one
        Assert.Equal(1, h.Ok);      // the 30-day one
    }

    [Fact]
    public void Summarize_AllHealthy_NoAlert()
    {
        var h = CertExpiry.Summarize(new[] { CertExpiringInDays("a", 60), CertExpiringInDays("b", 45) }, Now);
        Assert.False(h.HasAlert);
        Assert.False(h.HasCritical);
        Assert.Equal(2, h.Ok);
    }

    [Fact]
    public void Summarize_Empty_NoAlert()
    {
        var h = CertExpiry.Summarize(System.Array.Empty<Cert>(), Now);
        Assert.Equal(0, h.Total);
        Assert.False(h.HasAlert);
    }

    [Fact]
    public void SortByUrgency_SoonestFirst()
    {
        var certs = new[]
        {
            CertExpiringInDays("far", 90),
            CertExpiringInDays("soon", 3),
            CertExpiringInDays("mid", 25),
        };
        var sorted = CertExpiry.SortByUrgency(certs);
        Assert.Equal(new[] { "soon", "mid", "far" }, sorted.Select(c => c.Domain).ToArray());
    }

    [Fact]
    public void Summarize_UnknownExpiry_GoesToUnknownBucket_NotHealthyNorAlert()
    {
        var certs = new[]
        {
            CertExpiringInDays("ok", 60),
            new Cert("unknown.example", "Let's Encrypt", default, "managed", ExpiryKnown: false),
        };
        var h = CertExpiry.Summarize(certs, Now);
        Assert.Equal(1, h.Ok);
        Assert.Equal(1, h.Unknown);
        Assert.Equal(0, h.Warning);
        Assert.False(h.HasAlert);   // an unknown-expiry cert must not raise a false alert
    }

    [Fact]
    public void SortByUrgency_UnknownExpiryLast()
    {
        var certs = new[]
        {
            new Cert("unknown", "x", default, "managed", ExpiryKnown: false),
            CertExpiringInDays("soon", 3),
        };
        var sorted = CertExpiry.SortByUrgency(certs);
        Assert.Equal(new[] { "soon", "unknown" }, sorted.Select(c => c.Domain).ToArray());
    }
}
