using System.Text.Json;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class ServerConfigPatchTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ProtocolsArray_EmitsCheckedSubsetInOrder()
    {
        var r = Parse(ServerConfigPatch.ProtocolsArray(h1: true, h2: false, h3: true));
        Assert.Equal(JsonValueKind.Array, r.ValueKind);
        Assert.Equal(2, r.GetArrayLength());
        Assert.Equal("h1", r[0].GetString());
        Assert.Equal("h3", r[1].GetString());
    }

    [Fact]
    public void ProtocolsArray_NoneChecked_IsEmptyArray()
    {
        var r = Parse(ServerConfigPatch.ProtocolsArray(false, false, false));
        Assert.Equal(0, r.GetArrayLength());
    }

    [Fact]
    public void AutomaticHttps_EmitsOnlyTrueFlags_PlusSkip()
    {
        var r = Parse(ServerConfigPatch.AutomaticHttps(disable: true, disableRedirects: false,
            disableCerts: false, skip: new[] { "internal.host" }));
        Assert.True(r.GetProperty("disable").GetBoolean());
        Assert.False(r.TryGetProperty("disable_redirects", out _));
        Assert.Equal("internal.host", r.GetProperty("skip")[0].GetString());
    }

    [Fact]
    public void AutomaticHttps_AllFalseNoSkip_IsEmptyObject()
    {
        var r = Parse(ServerConfigPatch.AutomaticHttps(false, false, false, System.Array.Empty<string>()));
        Assert.Equal(JsonValueKind.Object, r.ValueKind);
        Assert.False(r.TryGetProperty("disable", out _));
    }

    [Fact]
    public void AdminObject_WrapsListen()
    {
        var r = Parse(ServerConfigPatch.AdminObject("localhost:2019"));
        Assert.Equal("localhost:2019", r.GetProperty("listen").GetString());
    }

    [Fact]
    public void StringArray_FiltersWhitespace()
    {
        var r = Parse(ServerConfigPatch.StringArray(new[] { ":8443", "", "  ", ":443" }));
        Assert.Equal(2, r.GetArrayLength());
        Assert.Equal(":8443", r[0].GetString());
        Assert.Equal(":443", r[1].GetString());
    }
}
