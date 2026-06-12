using System.Text.Json;
using System.Text.Json.Nodes;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

/// <summary>
/// Tests the pure ConfigCandidateBuilder: it takes the current full-config JSON plus a
/// batch of RouteOps and returns a NEW config JSON with all ops applied in-memory — or
/// throws CandidateBuildException (applying nothing). No HTTP, no UI.
///
/// In-memory mutation is what makes the batch transactional and removes the historical
/// array-index re-pathing hazard: ops are applied to a live tree, so a delete at a lower
/// index naturally shifts the elements above it for a later field write in the same batch.
/// </summary>
public class ConfigCandidateBuilderTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>Parse a JSON string and return the node at a slash-delimited path (for assertions).</summary>
    private static JsonNode? At(string json, string path)
    {
        JsonNode? node = JsonNode.Parse(json);
        foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node is JsonArray arr && int.TryParse(seg, out var i))
                node = (i >= 0 && i < arr.Count) ? arr[i] : null;
            else if (node is JsonObject obj)
                node = obj.TryGetPropertyValue(seg, out var v) ? v : null;
            else
                return null;
            if (node is null) return null;
        }
        return node;
    }

    private static RouteOp Field(string path, string json) =>
        RouteOp.Field(new PendingWrite(path, json, "{}", "field:" + path));

    // ── Field ops ────────────────────────────────────────────────────────

    [Fact]
    public void Field_ReplacesExistingScalar()
    {
        var ops = new[] { Field("apps/http/servers/srv0/routes/0/terminal", "false") };

        var result = ConfigCandidateBuilder.Apply(Fixture("config.json"), ops);

        Assert.False(At(result, "apps/http/servers/srv0/routes/0/terminal")!.GetValue<bool>());
    }

    [Fact]
    public void Field_ReplacesExistingObjectWholesale()
    {
        // Replace the route's match array with a host+path matcher.
        var newMatch = """[{"host":["new.example.com"],"path":["/api/*"]}]""";
        var ops = new[] { Field("apps/http/servers/srv0/routes/0/match", newMatch) };

        var result = ConfigCandidateBuilder.Apply(Fixture("config.json"), ops);

        Assert.Equal("new.example.com",
            At(result, "apps/http/servers/srv0/routes/0/match/0/host/0")!.GetValue<string>());
        Assert.Equal("/api/*",
            At(result, "apps/http/servers/srv0/routes/0/match/0/path/0")!.GetValue<string>());
    }

    [Fact]
    public void Field_CreatesAbsentNode_Upsert()
    {
        // "errors" is not present on srv0 in the fixture; a Field write should create it.
        var ops = new[] { Field("apps/http/servers/srv0/errors", """{"routes":[]}""") };

        var result = ConfigCandidateBuilder.Apply(Fixture("config.json"), ops);

        Assert.NotNull(At(result, "apps/http/servers/srv0/errors/routes"));
    }

    [Fact]
    public void Field_DeepNestedPath_ResolvesCorrectParent()
    {
        var path = "apps/http/servers/srv0/routes/0/handle/0/routes/0/handle/0/upstreams";
        var ops = new[] { Field(path, """[{"dial":"127.0.0.1:9999"}]""") };

        var result = ConfigCandidateBuilder.Apply(Fixture("config.json"), ops);

        Assert.Equal("127.0.0.1:9999", At(result, path + "/0/dial")!.GetValue<string>());
    }

    [Fact]
    public void Field_DoesNotMutateInputString()
    {
        var input = Fixture("config.json");
        var before = input;
        ConfigCandidateBuilder.Apply(input, new[] { Field("apps/http/servers/srv0/routes/0/terminal", "false") });
        Assert.Equal(before, input); // input string is untouched
    }

    // ── Add ops ──────────────────────────────────────────────────────────

    [Fact]
    public void Add_AppendsHandlerToArray_OthersIntact()
    {
        var arr = "apps/http/servers/srv0/routes/0/handle/0/routes/0/handle";
        var before = (JsonArray)At(Fixture("config.json"), arr)!;
        var beforeCount = before.Count;
        var ops = new[] { RouteOp.Add(arr, """{"handler":"encode"}""", "add encode") };

        var result = ConfigCandidateBuilder.Apply(Fixture("config.json"), ops);

        var after = (JsonArray)At(result, arr)!;
        Assert.Equal(beforeCount + 1, after.Count);
        Assert.Equal("encode", At(result, arr + "/" + beforeCount + "/handler")!.GetValue<string>());
        // pre-existing first element still the reverse_proxy
        Assert.Equal("reverse_proxy", At(result, arr + "/0/handler")!.GetValue<string>());
    }

    [Fact]
    public void Add_AppendsRoute()
    {
        var arr = "apps/http/servers/srv0/routes";
        var ops = new[] { RouteOp.Add(arr, """{"match":[{"host":["b.example.com"]}],"handle":[],"terminal":true}""", "add route") };

        var result = ConfigCandidateBuilder.Apply(Fixture("config.json"), ops);

        Assert.Equal(2, ((JsonArray)At(result, arr)!).Count);
        Assert.Equal("b.example.com", At(result, arr + "/1/match/0/host/0")!.GetValue<string>());
    }

    [Fact]
    public void Add_NonArrayPath_Throws()
    {
        // terminal is a bool, not an array.
        var ops = new[] { RouteOp.Add("apps/http/servers/srv0/routes/0/terminal", "{}", "bad add") };

        Assert.Throws<CandidateBuildException>(() => ConfigCandidateBuilder.Apply(Fixture("config.json"), ops));
    }

    // ── Insert ops (positional) ──────────────────────────────────────────

    [Fact]
    public void Insert_AtIndex_ShiftsSubsequentElementsDown()
    {
        var arr = "apps/http/servers/srv0/routes";
        // routes currently has one element (index 0). Insert a new route at index 0,
        // pushing the existing route to index 1.
        var ops = new[] { RouteOp.Insert(arr, 0, """{"match":[{"host":["deny.example.com"]}],"terminal":true}""", "insert deny") };

        var result = ConfigCandidateBuilder.Apply(Fixture("config.json"), ops);

        Assert.Equal(2, ((JsonArray)At(result, arr)!).Count);
        Assert.Equal("deny.example.com", At(result, arr + "/0/match/0/host/0")!.GetValue<string>());
        // the original route shifted to index 1
        Assert.Equal("example.com", At(result, arr + "/1/match/0/host/0")!.GetValue<string>());
    }

    [Fact]
    public void Insert_AtCount_AppendsAtEnd()
    {
        var arr = "apps/http/servers/srv0/routes";
        var ops = new[] { RouteOp.Insert(arr, 1, """{"terminal":true}""", "insert at end") };

        var result = ConfigCandidateBuilder.Apply(Fixture("config.json"), ops);

        Assert.Equal(2, ((JsonArray)At(result, arr)!).Count);
        Assert.True(At(result, arr + "/1/terminal")!.GetValue<bool>());
    }

    [Fact]
    public void Insert_IndexBeyondCount_Throws()
    {
        var arr = "apps/http/servers/srv0/routes";
        var ops = new[] { RouteOp.Insert(arr, 5, "{}", "too far") };

        Assert.Throws<CandidateBuildException>(() => ConfigCandidateBuilder.Apply(Fixture("config.json"), ops));
    }

    [Fact]
    public void Insert_NonArrayPath_Throws()
    {
        var ops = new[] { RouteOp.Insert("apps/http/servers/srv0/routes/0/terminal", 0, "{}", "bad") };

        Assert.Throws<CandidateBuildException>(() => ConfigCandidateBuilder.Apply(Fixture("config.json"), ops));
    }

    // ── Delete ops ───────────────────────────────────────────────────────

    [Fact]
    public void Delete_RemovesArrayElement_SiblingsReindex()
    {
        var arr = "apps/http/servers/srv0/routes/0/handle/0/routes/0/handle";
        // Append two so we have indices 0,1,2 then delete index 1.
        var seed = new[]
        {
            RouteOp.Add(arr, """{"handler":"a"}""", "a"),
            RouteOp.Add(arr, """{"handler":"b"}""", "b"),
        };
        var seeded = ConfigCandidateBuilder.Apply(Fixture("config.json"), seed);
        // now handle = [reverse_proxy, a, b]; delete index 1 (a)
        var result = ConfigCandidateBuilder.Apply(seeded, new[] { RouteOp.Delete(arr + "/1", "{}", "del a") });

        var after = (JsonArray)At(result, arr)!;
        Assert.Equal(2, after.Count);
        Assert.Equal("reverse_proxy", At(result, arr + "/0/handler")!.GetValue<string>());
        Assert.Equal("b", At(result, arr + "/1/handler")!.GetValue<string>()); // b shifted 2 -> 1
    }

    [Fact]
    public void Delete_RemovesRouteElement()
    {
        var arr = "apps/http/servers/srv0/routes";
        var result = ConfigCandidateBuilder.Apply(Fixture("config.json"), new[] { RouteOp.Delete(arr + "/0", "{}", "del route") });

        Assert.Empty((JsonArray)At(result, arr)!);
    }

    [Fact]
    public void Delete_OutOfRangeIndex_Throws()
    {
        var ops = new[] { RouteOp.Delete("apps/http/servers/srv0/routes/0/handle/0/routes/0/handle/9", "{}", "oob") };

        Assert.Throws<CandidateBuildException>(() => ConfigCandidateBuilder.Apply(Fixture("config.json"), ops));
    }

    [Fact]
    public void Delete_NonExistentObjectKey_Throws()
    {
        var ops = new[] { RouteOp.Delete("apps/http/servers/srv0/nope", "{}", "missing key") };

        Assert.Throws<CandidateBuildException>(() => ConfigCandidateBuilder.Apply(Fixture("config.json"), ops));
    }

    // ── Multi-op: the transactional core ─────────────────────────────────

    [Fact]
    public void MultiOp_DeleteThenFieldInSameArray_FieldLandsOnIntendedNode()
    {
        // The historical re-path hazard. handle = [reverse_proxy, x, y].
        var arr = "apps/http/servers/srv0/routes/0/handle/0/routes/0/handle";
        var seeded = ConfigCandidateBuilder.Apply(Fixture("config.json"), new[]
        {
            RouteOp.Add(arr, """{"handler":"x"}""", "x"),
            RouteOp.Add(arr, """{"handler":"y","tag":"original"}""", "y"),
        });
        // Delete index 1 (x), AND set a field on what was index 2 (y). With in-memory mutation,
        // operating on the live tree, the field op must target y after the delete shifts it to 1.
        // The caller emits the field at y's ORIGINAL index (2) — builder applies delete first, so
        // we express that explicitly: delete then field-at-the-post-delete index.
        var result = ConfigCandidateBuilder.Apply(seeded, new[]
        {
            RouteOp.Delete(arr + "/1", "{}", "del x"),
            Field(arr + "/1/tag", "\"changed\""), // after delete, y is at index 1
        });

        var after = (JsonArray)At(result, arr)!;
        Assert.Equal(2, after.Count);
        Assert.Equal("y", At(result, arr + "/1/handler")!.GetValue<string>());
        Assert.Equal("changed", At(result, arr + "/1/tag")!.GetValue<string>());
    }

    [Fact]
    public void MultiOp_AddDeleteField_Mixed_MatchesExpected()
    {
        var arr = "apps/http/servers/srv0/routes/0/handle/0/routes/0/handle";
        // Start: [reverse_proxy]. Add 'rewrite' -> [reverse_proxy, rewrite].
        // Delete index 0 (reverse_proxy) -> [rewrite]. Field set on index 0 -> rewrite.uri.
        var result = ConfigCandidateBuilder.Apply(Fixture("config.json"), new[]
        {
            RouteOp.Add(arr, """{"handler":"rewrite"}""", "add rewrite"),
            RouteOp.Delete(arr + "/0", "{}", "del rp"),
            Field(arr + "/0/uri", "\"/new\""),
        });

        var after = (JsonArray)At(result, arr)!;
        Assert.Single(after);
        Assert.Equal("rewrite", At(result, arr + "/0/handler")!.GetValue<string>());
        Assert.Equal("/new", At(result, arr + "/0/uri")!.GetValue<string>());
    }

    [Fact]
    public void EmptyBatch_ReturnsSemanticallyEqualConfig()
    {
        var input = Fixture("config.json");

        var result = ConfigCandidateBuilder.Apply(input, Array.Empty<RouteOp>());

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(input), JsonNode.Parse(result)));
    }

    [Fact]
    public void Field_Idempotent_AppliedTwiceSameResult()
    {
        var op = Field("apps/http/servers/srv0/routes/0/terminal", "false");
        var once = ConfigCandidateBuilder.Apply(Fixture("config.json"), new[] { op });
        var twice = ConfigCandidateBuilder.Apply(once, new[] { op });

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(once), JsonNode.Parse(twice)));
    }

    // ── Failure isolation ────────────────────────────────────────────────

    [Fact]
    public void OneInvalidOpInBatch_ThrowsAndAppliesNothing()
    {
        // A valid field followed by an invalid delete. Apply is all-or-nothing: it throws,
        // and (since it returns a string only on full success) the caller never gets a
        // partially-applied candidate to /load.
        var ops = new[]
        {
            Field("apps/http/servers/srv0/routes/0/terminal", "false"),
            RouteOp.Delete("apps/http/servers/srv0/routes/0/handle/0/routes/0/handle/9", "{}", "oob"),
        };

        Assert.Throws<CandidateBuildException>(() => ConfigCandidateBuilder.Apply(Fixture("config.json"), ops));
    }

    [Fact]
    public void MalformedFragmentJson_Throws()
    {
        var ops = new[] { Field("apps/http/servers/srv0/routes/0/terminal", "{not json") };

        var ex = Assert.Throws<CandidateBuildException>(() => ConfigCandidateBuilder.Apply(Fixture("config.json"), ops));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void MalformedCurrentConfig_Throws()
    {
        var ops = new[] { Field("a", "1") };

        Assert.Throws<CandidateBuildException>(() => ConfigCandidateBuilder.Apply("{not valid", ops));
    }

    [Fact]
    public void Field_PathThroughScalar_Throws()
    {
        // terminal is a bool; descending into it is invalid.
        var ops = new[] { Field("apps/http/servers/srv0/routes/0/terminal/x", "1") };

        Assert.Throws<CandidateBuildException>(() => ConfigCandidateBuilder.Apply(Fixture("config.json"), ops));
    }
}
