using System.Text.Json;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class HandlerPatchD3Tests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Browse_EmptyInput_IsEmptyObject()
    {
        var json = HandlerPatch.Browse(new BrowseInput("", false, System.Array.Empty<string>(), 0));
        var r = Parse(json);
        Assert.Equal(JsonValueKind.Object, r.ValueKind);
        Assert.False(r.TryGetProperty("template_file", out _));
        Assert.False(r.TryGetProperty("reveal_symlinks", out _));
        Assert.False(r.TryGetProperty("sort", out _));
        Assert.False(r.TryGetProperty("file_limit", out _));
    }

    [Fact]
    public void Browse_PopulatedFields_AreEmitted()
    {
        var json = HandlerPatch.Browse(new BrowseInput("/tpl.html", true, new[] { "name", "asc" }, 100));
        var r = Parse(json);
        Assert.Equal("/tpl.html", r.GetProperty("template_file").GetString());
        Assert.True(r.GetProperty("reveal_symlinks").GetBoolean());
        Assert.Equal(2, r.GetProperty("sort").GetArrayLength());
        Assert.Equal("name", r.GetProperty("sort")[0].GetString());
        Assert.Equal(100, r.GetProperty("file_limit").GetInt32());
    }

    [Fact]
    public void FileServer_ExtraFields_Emitted_AndBrowseNotEmittedByBuilder()
    {
        var json = HandlerPatch.FileServer(new FileServerInput(
            Root: "/var/www", IndexNames: new[] { "index.html" }, Hide: System.Array.Empty<string>(),
            PassThru: true, PrecompressedOrder: new[] { "br", "gzip" },
            StatusCode: "404", CanonicalUrisSet: true, CanonicalUris: false));
        var r = Parse(json);
        Assert.Equal("file_server", r.GetProperty("handler").GetString());
        Assert.Equal("/var/www", r.GetProperty("root").GetString());
        Assert.True(r.GetProperty("pass_thru").GetBoolean());
        Assert.Equal(2, r.GetProperty("precompressed_order").GetArrayLength());
        Assert.Equal("404", r.GetProperty("status_code").GetString());
        Assert.False(r.GetProperty("canonical_uris").GetBoolean());
        Assert.False(r.TryGetProperty("browse", out _));
    }

    [Fact]
    public void FileServer_CanonicalUrisNotSet_OmitsKey()
    {
        var json = HandlerPatch.FileServer(new FileServerInput(
            "/x", System.Array.Empty<string>(), System.Array.Empty<string>(), false,
            System.Array.Empty<string>(), "", CanonicalUrisSet: false, CanonicalUris: false));
        var r = Parse(json);
        Assert.False(r.TryGetProperty("canonical_uris", out _));
        Assert.False(r.TryGetProperty("status_code", out _));
        Assert.False(r.TryGetProperty("precompressed_order", out _));
    }

    [Fact]
    public void FileServer_NeverEmitsPolymorphicKeys()
    {
        var json = HandlerPatch.FileServer(new FileServerInput(
            "/x", new[] { "i" }, new[] { "h" }, true, new[] { "br" }, "200", true, true));
        var r = Parse(json);
        Assert.False(r.TryGetProperty("precompressed", out _));
        Assert.False(r.TryGetProperty("fs", out _));
        Assert.False(r.TryGetProperty("etag_file_extensions", out _));
    }

    [Fact]
    public void Headers_Replace_LiteralAndRegex_Emitted()
    {
        var req = new HeaderOpsInput(
            System.Array.Empty<(string, string)>(), System.Array.Empty<(string, string)>(),
            System.Array.Empty<string>(),
            new[] { ("X-Url", "http://old", "http://new", false), ("Location", "secret", "***", true) });
        var resp = HeaderOpsInput.Empty;
        var json = HandlerPatch.Headers(req, resp, null);
        var r = Parse(json);
        var rep = r.GetProperty("request").GetProperty("replace");
        Assert.Equal("http://old", rep.GetProperty("X-Url")[0].GetProperty("search").GetString());
        Assert.Equal("http://new", rep.GetProperty("X-Url")[0].GetProperty("replace").GetString());
        Assert.Equal("secret", rep.GetProperty("Location")[0].GetProperty("search_regexp").GetString());
    }

    [Fact]
    public void Headers_ResponseRequire_Emitted()
    {
        var require = new ResponseRequireInput(new[] { 200, 204 },
            new[] { ("Content-Type", "application/json") });
        var json = HandlerPatch.Headers(HeaderOpsInput.Empty, HeaderOpsInput.Empty, require);
        var r = Parse(json);
        var req = r.GetProperty("response").GetProperty("require");
        Assert.Equal(2, req.GetProperty("status_code").GetArrayLength());
        Assert.Equal(200, req.GetProperty("status_code")[0].GetInt32());
        Assert.Equal("application/json", req.GetProperty("headers").GetProperty("Content-Type")[0].GetString());
    }

    [Fact]
    public void Headers_NoRequire_OmitsRequire()
    {
        var json = HandlerPatch.Headers(HeaderOpsInput.Empty, HeaderOpsInput.Empty, null);
        var r = Parse(json);
        Assert.False(r.TryGetProperty("response", out _));
    }

    [Fact]
    public void ManagedFileServerKeys_CoversEmittedKeys_ExcludesBrowseAndPolymorphic()
    {
        var keys = HandlerPatch.ManagedFileServerKeys;
        foreach (var k in new[] { "handler", "root", "index_names", "hide", "pass_thru",
                                  "precompressed_order", "status_code", "canonical_uris" })
            Assert.Contains(k, keys);
        Assert.DoesNotContain("browse", keys);
        Assert.DoesNotContain("precompressed", keys);
        Assert.DoesNotContain("fs", keys);
    }
}
