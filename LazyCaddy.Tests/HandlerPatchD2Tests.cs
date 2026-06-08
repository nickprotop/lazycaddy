using System.Text.Json;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class HandlerPatchD2Tests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void HttpTransport_EmptyInput_EmitsOnlyProtocol()
    {
        var json = HandlerPatch.HttpTransport(new HttpTransportInput(
            Compression: false, MaxConnsPerHost: 0, DialTimeout: "", DialFallbackDelay: "",
            ResponseHeaderTimeout: "", ExpectContinueTimeout: "", ReadTimeout: "", WriteTimeout: "",
            MaxResponseHeaderSize: 0, ReadBufferSize: 0, WriteBufferSize: 0,
            Versions: System.Array.Empty<string>(), LocalAddress: "", ProxyProtocol: "",
            ResolverAddresses: System.Array.Empty<string>()));
        var r = Parse(json);
        Assert.Equal("http", r.GetProperty("protocol").GetString());
        Assert.False(r.TryGetProperty("dial_timeout", out _));
        Assert.False(r.TryGetProperty("compression", out _));
        Assert.False(r.TryGetProperty("resolver", out _));
        Assert.False(r.TryGetProperty("versions", out _));
    }

    [Fact]
    public void HttpTransport_PopulatedFields_AreEmitted()
    {
        var json = HandlerPatch.HttpTransport(new HttpTransportInput(
            Compression: true, MaxConnsPerHost: 50, DialTimeout: "10s", DialFallbackDelay: "300ms",
            ResponseHeaderTimeout: "5s", ExpectContinueTimeout: "1s", ReadTimeout: "30s", WriteTimeout: "30s",
            MaxResponseHeaderSize: 8192, ReadBufferSize: 4096, WriteBufferSize: 4096,
            Versions: new[] { "1.1", "2" }, LocalAddress: "10.0.0.1", ProxyProtocol: "v2",
            ResolverAddresses: new[] { "8.8.8.8", "1.1.1.1" }));
        var r = Parse(json);
        Assert.Equal("http", r.GetProperty("protocol").GetString());
        Assert.True(r.GetProperty("compression").GetBoolean());
        Assert.Equal(50, r.GetProperty("max_conns_per_host").GetInt32());
        Assert.Equal("10s", r.GetProperty("dial_timeout").GetString());
        Assert.Equal("v2", r.GetProperty("proxy_protocol").GetString());
        Assert.Equal(2, r.GetProperty("versions").GetArrayLength());
        Assert.Equal("1.1", r.GetProperty("versions")[0].GetString());
        Assert.Equal(2, r.GetProperty("resolver").GetProperty("addresses").GetArrayLength());
    }

    [Fact]
    public void HttpTransport_NeverEmitsPolymorphicKeys()
    {
        var json = HandlerPatch.HttpTransport(new HttpTransportInput(
            true, 1, "1s", "1s", "1s", "1s", "1s", "1s", 1, 1, 1,
            new[] { "2" }, "x", "v1", new[] { "1.1.1.1" }));
        var r = Parse(json);
        Assert.False(r.TryGetProperty("network_proxy", out _));
        Assert.False(r.TryGetProperty("tls", out _));
        Assert.False(r.TryGetProperty("keep_alive", out _));
    }

    [Fact]
    public void TlsConfig_OmitsEmpty_KeepsSet()
    {
        var json = HandlerPatch.TlsConfig(new TlsConfigInput(
            InsecureSkipVerify: true, ServerName: "backend.internal", Renegotiation: "once",
            HandshakeTimeout: "10s", Curves: new[] { "x25519" }, ExceptPorts: System.Array.Empty<string>()));
        var r = Parse(json);
        Assert.True(r.GetProperty("insecure_skip_verify").GetBoolean());
        Assert.Equal("backend.internal", r.GetProperty("server_name").GetString());
        Assert.Equal("once", r.GetProperty("renegotiation").GetString());
        Assert.Equal(1, r.GetProperty("curves").GetArrayLength());
        Assert.False(r.TryGetProperty("except_ports", out _));
        Assert.False(r.TryGetProperty("ca", out _));
    }

    [Fact]
    public void TlsConfig_AllEmpty_IsEmptyObject()
    {
        var json = HandlerPatch.TlsConfig(new TlsConfigInput(false, "", "", "",
            System.Array.Empty<string>(), System.Array.Empty<string>()));
        var r = Parse(json);
        Assert.Equal(JsonValueKind.Object, r.ValueKind);
        Assert.False(r.TryGetProperty("insecure_skip_verify", out _));
    }

    [Fact]
    public void KeepAlive_EnabledFalse_IsEmittedExplicitly()
    {
        var json = HandlerPatch.KeepAlive(new KeepAliveInput(
            EnabledSet: true, Enabled: false, IdleTimeout: "", ProbeInterval: "",
            MaxIdleConns: 0, MaxIdleConnsPerHost: 0));
        var r = Parse(json);
        Assert.False(r.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void KeepAlive_EnabledNotSet_OmitsEnabled()
    {
        var json = HandlerPatch.KeepAlive(new KeepAliveInput(
            EnabledSet: false, Enabled: false, IdleTimeout: "2m", ProbeInterval: "",
            MaxIdleConns: 100, MaxIdleConnsPerHost: 10));
        var r = Parse(json);
        Assert.False(r.TryGetProperty("enabled", out _));
        Assert.Equal("2m", r.GetProperty("idle_timeout").GetString());
        Assert.Equal(100, r.GetProperty("max_idle_conns").GetInt32());
        Assert.Equal(10, r.GetProperty("max_idle_conns_per_host").GetInt32());
    }

    [Fact]
    public void MergeTransport_PreservesTlsAndKeepAlive()
    {
        var original = """{"protocol":"http","dial_timeout":"5s","tls":{"server_name":"x"},"keep_alive":{"enabled":false}}""";
        var managed = """{"protocol":"http","dial_timeout":"9s"}""";
        var r = Parse(HandlerPatch.MergeTransport(original, managed));
        Assert.Equal("9s", r.GetProperty("dial_timeout").GetString());
        Assert.Equal("x", r.GetProperty("tls").GetProperty("server_name").GetString());
        Assert.False(r.GetProperty("keep_alive").GetProperty("enabled").GetBoolean());
        // A managed key the user cleared (omitted from managed) must not be resurrected from original.
        var original2 = """{"protocol":"http","read_timeout":"3s","tls":{"server_name":"x"}}""";
        var r2 = Parse(HandlerPatch.MergeTransport(original2, managed));
        Assert.False(r2.TryGetProperty("read_timeout", out _));
        Assert.True(r2.TryGetProperty("tls", out _));
    }

    [Fact]
    public void MergeTransport_PreservesUnknownKey()
    {
        var original = """{"protocol":"http","network_proxy":{"foo":1}}""";
        var managed = """{"protocol":"http"}""";
        var r = Parse(HandlerPatch.MergeTransport(original, managed));
        Assert.Equal(1, r.GetProperty("network_proxy").GetProperty("foo").GetInt32());
    }

    [Fact]
    public void MergeTransport_ManagedScalarsWin()
    {
        var original = """{"dial_timeout":"5s"}""";
        var managed = """{"protocol":"http","dial_timeout":"9s"}""";
        var r = Parse(HandlerPatch.MergeTransport(original, managed));
        Assert.Equal("9s", r.GetProperty("dial_timeout").GetString());
    }

    [Fact]
    public void MergeTransport_EmptyOriginal_ReturnsManaged()
    {
        var managed = """{"protocol":"http","dial_timeout":"9s"}""";
        var r = Parse(HandlerPatch.MergeTransport("{}", managed));
        Assert.Equal("http", r.GetProperty("protocol").GetString());
        Assert.Equal("9s", r.GetProperty("dial_timeout").GetString());
        Assert.Equal(2, r.EnumerateObject().Count());
    }

    [Fact]
    public void MergeTransport_NonObjectOriginal_ReturnsManaged()
    {
        var managed = """{"protocol":"http"}""";
        Assert.Equal(managed, HandlerPatch.MergeTransport("null", managed));
        Assert.Equal(managed, HandlerPatch.MergeTransport("[]", managed));
        Assert.Equal(managed, HandlerPatch.MergeTransport("", managed));
        Assert.Equal(managed, HandlerPatch.MergeTransport(null!, managed));
    }

    [Fact]
    public void MergeTlsConfig_PreservesCaAndClientCert()
    {
        var original = """{"server_name":"old","ca":{"provider":"x"},"client_certificate_file":"/c.pem"}""";
        var managed = """{"server_name":"new","insecure_skip_verify":true}""";
        var r = Parse(HandlerPatch.MergeTlsConfig(original, managed));
        Assert.Equal("new", r.GetProperty("server_name").GetString());
        Assert.True(r.GetProperty("insecure_skip_verify").GetBoolean());
        Assert.Equal("x", r.GetProperty("ca").GetProperty("provider").GetString());
        Assert.Equal("/c.pem", r.GetProperty("client_certificate_file").GetString());
    }

    [Fact]
    public void MergeTlsConfig_ManagedKeyClearedNotResurrected()
    {
        var original = """{"server_name":"old","ca":{"p":1}}""";
        var managed = "{}";
        var r = Parse(HandlerPatch.MergeTlsConfig(original, managed));
        Assert.False(r.TryGetProperty("server_name", out _));
        Assert.Equal(1, r.GetProperty("ca").GetProperty("p").GetInt32());
    }

    [Fact]
    public void MergeUnmanaged_NonObjectOriginal_ReturnsManaged()
    {
        var managed = """{"server_name":"new"}""";
        var keys = new HashSet<string> { "server_name" };
        Assert.Equal(
            JsonSerializer.Serialize(Parse(managed)),
            JsonSerializer.Serialize(Parse(HandlerPatch.MergeUnmanaged("null", managed, keys))));
    }
}
