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

    [Fact]
    public void LogWriter_File_EmitsOutputAndFilename()
    {
        var r = Parse(ServerConfigPatch.LogWriter("file", "/var/log/x.log"));
        Assert.Equal("file", r.GetProperty("output").GetString());
        Assert.Equal("/var/log/x.log", r.GetProperty("filename").GetString());
    }

    [Fact]
    public void LogWriter_Stdout_EmitsOnlyOutput()
    {
        var r = Parse(ServerConfigPatch.LogWriter("stdout", ""));
        Assert.Equal("stdout", r.GetProperty("output").GetString());
        Assert.False(r.TryGetProperty("filename", out _));
    }

    [Fact]
    public void LogNode_OmitsEmpty_KeepsSet()
    {
        var r = Parse(ServerConfigPatch.LogNode("DEBUG",
            include: new[] { "http.log.access" }, exclude: System.Array.Empty<string>(),
            writerJson: ServerConfigPatch.LogWriter("stdout", "")));
        Assert.Equal("DEBUG", r.GetProperty("level").GetString());
        Assert.Equal("http.log.access", r.GetProperty("include")[0].GetString());
        Assert.False(r.TryGetProperty("exclude", out _));
        Assert.Equal("stdout", r.GetProperty("writer").GetProperty("output").GetString());
    }

    [Fact]
    public void LogNode_NoWriter_OmitsWriter()
    {
        var r = Parse(ServerConfigPatch.LogNode("", System.Array.Empty<string>(), System.Array.Empty<string>(), ""));
        Assert.Equal(JsonValueKind.Object, r.ValueKind);
        Assert.False(r.TryGetProperty("writer", out _));
        Assert.False(r.TryGetProperty("level", out _));
    }

    // Contract for LogOutputDialog's "(unchanged)" writer path: an unknown/polymorphic writer
    // module (one the form can't represent) must SURVIVE a level-only edit, because the managed
    // key set then EXCLUDES "writer" so MergeUnmanaged copies the original writer verbatim.
    private static readonly System.Collections.Generic.HashSet<string> ManagedLogKeysNoWriter =
        new() { "level", "include", "exclude" };
    private static readonly System.Collections.Generic.HashSet<string> ManagedLogKeysWithWriter =
        new() { "level", "include", "exclude", "writer" };

    [Fact]
    public void LogNode_UnknownWriter_PreservedThroughMerge_WhenOutputUnchanged()
    {
        var original = "{\"level\":\"INFO\",\"writer\":{\"output\":\"net\",\"address\":\"localhost:514\"}}";
        var managed = ServerConfigPatch.LogNode("DEBUG",
            System.Array.Empty<string>(), System.Array.Empty<string>(), ""); // no writer formed
        var merged = Parse(HandlerPatch.MergeUnmanaged(original, managed, ManagedLogKeysNoWriter));
        Assert.Equal("DEBUG", merged.GetProperty("level").GetString());          // managed wins for level
        Assert.Equal("net", merged.GetProperty("writer").GetProperty("output").GetString());     // unknown writer kept
        Assert.Equal("localhost:514", merged.GetProperty("writer").GetProperty("address").GetString());
    }

    [Fact]
    public void LogNode_KnownWriter_ReplacesOriginal_WhenOutputChosen()
    {
        var original = "{\"writer\":{\"output\":\"net\",\"address\":\"localhost:514\"}}";
        var managed = ServerConfigPatch.LogNode("",
            System.Array.Empty<string>(), System.Array.Empty<string>(),
            ServerConfigPatch.LogWriter("stdout", ""));
        var merged = Parse(HandlerPatch.MergeUnmanaged(original, managed, ManagedLogKeysWithWriter));
        Assert.Equal("stdout", merged.GetProperty("writer").GetProperty("output").GetString()); // replaced
        Assert.False(merged.GetProperty("writer").TryGetProperty("address", out _));             // old field gone
    }
}
