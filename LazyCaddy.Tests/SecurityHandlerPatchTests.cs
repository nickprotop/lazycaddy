using System.Text.Json;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class SecurityHandlerPatchTests
{
    private static JsonElement Parse(string j) { using var d = JsonDocument.Parse(j); return d.RootElement.Clone(); }

    [Fact]
    public void BasicAuth_BuildsProviderWithAccounts()
    {
        var json = SecurityHandlerPatch.BasicAuth("restricted", new[] { ("alice", "$2a$14$hash") });
        var e = Parse(json);
        Assert.Equal("authentication", e.GetProperty("handler").GetString());
        var hb = e.GetProperty("providers").GetProperty("http_basic");
        Assert.Equal("restricted", hb.GetProperty("realm").GetString());
        var acc = hb.GetProperty("accounts")[0];
        Assert.Equal("alice", acc.GetProperty("username").GetString());
        Assert.Equal("$2a$14$hash", acc.GetProperty("password").GetString());
        Assert.Equal("bcrypt", hb.GetProperty("hash").GetProperty("algorithm").GetString());
    }

    [Fact]
    public void SecurityHeaders_SetsOnlyEnabledHeaders()
    {
        var json = SecurityHandlerPatch.SecurityHeaders("max-age=31536000; includeSubDomains", true, "DENY", "strict-origin-when-cross-origin", null);
        var set = Parse(json).GetProperty("response").GetProperty("set");
        Assert.Equal("max-age=31536000; includeSubDomains", set.GetProperty("Strict-Transport-Security")[0].GetString());
        Assert.Equal("nosniff", set.GetProperty("X-Content-Type-Options")[0].GetString());
        Assert.Equal("DENY", set.GetProperty("X-Frame-Options")[0].GetString());
        Assert.Equal("strict-origin-when-cross-origin", set.GetProperty("Referrer-Policy")[0].GetString());
        Assert.False(set.TryGetProperty("Content-Security-Policy", out _));
        Assert.Equal("headers", Parse(json).GetProperty("handler").GetString());
    }

    [Fact]
    public void RateLimit_BuildsZone()
    {
        var json = SecurityHandlerPatch.RateLimit("by_ip", "{http.request.remote.host}", "1m", 100);
        var z = Parse(json).GetProperty("rate_limits").GetProperty("by_ip");
        Assert.Equal("{http.request.remote.host}", z.GetProperty("key").GetString());
        Assert.Equal("1m", z.GetProperty("window").GetString());
        Assert.Equal(100, z.GetProperty("max_events").GetInt32());
        Assert.Equal("rate_limit", Parse(json).GetProperty("handler").GetString());
    }

    [Fact]
    public void ForwardAuth_Authelia_HasVerifyUriAndCopiesRemoteHeaders()
    {
        var json = SecurityHandlerPatch.ForwardAuth(ForwardAuthProvider.Authelia, "authelia:9091");
        var e = Parse(json);
        Assert.Equal("reverse_proxy", e.GetProperty("handler").GetString());
        Assert.Equal("authelia:9091", e.GetProperty("upstreams")[0].GetProperty("dial").GetString());
        Assert.Equal("/api/authz/forward-auth", e.GetProperty("rewrite").GetProperty("uri").GetString());
        Assert.True(e.TryGetProperty("handle_response", out _));
    }

    [Fact]
    public void IpMatcher_BuildsRemoteIpRanges()
    {
        var json = SecurityHandlerPatch.IpMatcher(false, new[] { "10.0.0.0/8", "192.168.0.0/16" });
        var m = Parse(json).GetProperty("remote_ip").GetProperty("ranges");
        Assert.Equal(2, m.GetArrayLength());
        Assert.Equal("10.0.0.0/8", m[0].GetString());
    }

    [Fact]
    public void IpMatcher_ClientIpVariant()
        => Assert.True(Parse(SecurityHandlerPatch.IpMatcher(true, new[]{"1.2.3.0/24"})).TryGetProperty("client_ip", out _));

    [Fact]
    public void DenyRoute_BuildsTerminal403()
    {
        var json = SecurityHandlerPatch.DenyRoute(false, new[] { "1.2.3.4/32" });
        var e = Parse(json);
        Assert.True(e.GetProperty("terminal").GetBoolean());
        Assert.Equal(403, e.GetProperty("handle")[0].GetProperty("status_code").GetInt32());
        Assert.Equal("1.2.3.4/32", e.GetProperty("match")[0].GetProperty("remote_ip").GetProperty("ranges")[0].GetString());
    }

    [Fact]
    public void TlsPolicy_BuildsMinMax()
    {
        var json = SecurityHandlerPatch.TlsPolicy("tls1.2", "tls1.3");
        var e = Parse(json);
        Assert.Equal("tls1.2", e.GetProperty("protocol_min").GetString());
        Assert.Equal("tls1.3", e.GetProperty("protocol_max").GetString());
    }
}
