using System.Text.Json.Nodes;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

/// <summary>
/// EditCoordinator now applies a route-op batch transactionally: read the current config,
/// compute the full candidate in-memory (ConfigCandidateBuilder), snapshot the known-good
/// config, and POST /load the candidate exactly once. Caddy's /load is all-or-nothing, so a
/// batch either fully lands or fully rolls back — there is no per-op sequencing to assert.
/// </summary>
public class EditCoordinatorOpsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lazycaddy-ops-" + Guid.NewGuid().ToString("N"));

    // A small but real config the builder can mutate: one route with a two-element handle array.
    private const string BaseConfig = """
    {"apps":{"http":{"servers":{"srv0":{"routes":[
      {"match":[{"host":["a.example.com"]}],
       "handle":[{"handler":"rewrite","uri":"/old"},{"handler":"reverse_proxy"}],
       "terminal":true}
    ]}}}}}
    """;

    /// <summary>ICaddyAdmin fake: serves a config, records every LoadConfigAsync payload,
    /// returns scripted /load results (default Ok). Granular write verbs throw — the edit path
    /// must not call them any more.</summary>
    private sealed class FakeAdmin : ICaddyAdmin
    {
        public string RawConfig = BaseConfig;
        public readonly List<string> LoadCalls = new();
        public Queue<WriteResult> LoadResults = new();

        public Task<string> GetRawConfigAsync(CancellationToken ct = default) => Task.FromResult(RawConfig);

        public Task<WriteResult> LoadConfigAsync(string fullConfigJson, CancellationToken ct = default)
        {
            LoadCalls.Add(fullConfigJson);
            return Task.FromResult(LoadResults.Count > 0 ? LoadResults.Dequeue() : WriteResult.Ok);
        }

        public Task<CaddyStatus> GetStatusAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Route>> GetRoutesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Cert>> GetCertsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Upstream>> GetUpstreamsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MetricsSnapshot> GetMetricsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetConfigNodeAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> PatchConfigAsync(string path, string json, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> PutConfigAsync(string path, string json, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> UpsertConfigAsync(string path, string json, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> PostConfigAsync(string path, string json, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> DeleteConfigAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AdaptResult> AdaptCaddyfileAsync(string caddyfile, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private (EditCoordinator coord, FakeAdmin fake, SnapshotStore store) NewCoordinator(bool readOnly = false)
    {
        var fake = new FakeAdmin();
        var store = new SnapshotStore(_dir, 50);
        var config = LazyCaddyConfig.Default with { ReadOnly = readOnly };
        return (new EditCoordinator(fake, store, config), fake, store);
    }

    private static RouteOp Field(string path, string json) =>
        RouteOp.Field(new PendingWrite(path, json, "{}", "field:" + path));

    private static JsonNode? At(string json, string path)
    {
        JsonNode? node = JsonNode.Parse(json);
        foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node is JsonArray arr && int.TryParse(seg, out var i)) node = (i >= 0 && i < arr.Count) ? arr[i] : null;
            else if (node is JsonObject obj) node = obj.TryGetPropertyValue(seg, out var v) ? v : null;
            else return null;
            if (node is null) return null;
        }
        return node;
    }

    private const string HandleArr = "apps/http/servers/srv0/routes/0/handle";

    [Fact]
    public async Task ApplyOps_Empty_NoSnapshotNoLoad()
    {
        var (coord, fake, store) = NewCoordinator();

        var result = await coord.ApplyOpsAsync(Array.Empty<RouteOp>(), "ops");

        Assert.Equal(0, result.Applied);
        Assert.Equal(0, result.Total);
        Assert.Null(result.Error);
        Assert.Empty(fake.LoadCalls);
        Assert.Empty(store.All());
    }

    [Fact]
    public async Task ApplyOps_AllSucceed_SingleLoadWithComputedCandidate()
    {
        var (coord, fake, _) = NewCoordinator();
        var ops = new[]
        {
            Field(HandleArr + "/0/uri", "\"/new\""),
            RouteOp.Add(HandleArr, """{"handler":"encode"}""", "add encode"),
        };

        var result = await coord.ApplyOpsAsync(ops, "ops");

        Assert.True(result.AllSucceeded);
        Assert.Equal(2, result.Applied);
        Assert.Equal(2, result.Total);
        // Exactly one /load with the fully-computed candidate.
        var candidate = Assert.Single(fake.LoadCalls);
        Assert.Equal("/new", At(candidate, HandleArr + "/0/uri")!.GetValue<string>());
        Assert.Equal("encode", At(candidate, HandleArr + "/2/handler")!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyOps_DeleteThenFieldSameArray_NoManualRepathNeeded()
    {
        // Delete handle[0] (rewrite), then set a field on the surviving reverse_proxy which is now
        // at index 0. In-memory mutation handles the shift; the field op targets the post-delete index.
        var (coord, fake, _) = NewCoordinator();
        var ops = new[]
        {
            RouteOp.Delete(HandleArr + "/0", "{}", "del rewrite"),
            Field(HandleArr + "/0/handler", "\"reverse_proxy_v2\""),
        };

        var result = await coord.ApplyOpsAsync(ops, "ops");

        Assert.True(result.AllSucceeded);
        var candidate = Assert.Single(fake.LoadCalls);
        Assert.Single((JsonArray)At(candidate, HandleArr)!);
        Assert.Equal("reverse_proxy_v2", At(candidate, HandleArr + "/0/handler")!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyOps_LoadFails_AppliedZero_ErrorSurfaced_SnapshotStillCaptured()
    {
        var (coord, fake, store) = NewCoordinator();
        fake.LoadResults.Enqueue(WriteResult.Fail("provision error: bad upstream"));
        var ops = new[] { Field(HandleArr + "/0/uri", "\"/x\"") };

        var result = await coord.ApplyOpsAsync(ops, "ops");

        Assert.False(result.AllSucceeded);
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Total);
        Assert.Contains("provision error", result.Error);
        Assert.Single(fake.LoadCalls);           // we did attempt the load
        Assert.Single(store.All());              // snapshot of the known-good config remains
    }

    [Fact]
    public async Task ApplyOps_InvalidOp_NoLoad_NoSnapshot()
    {
        var (coord, fake, store) = NewCoordinator();
        // Delete an out-of-range index → candidate build fails before any network call.
        var ops = new[] { RouteOp.Delete(HandleArr + "/9", "{}", "oob") };

        var result = await coord.ApplyOpsAsync(ops, "ops");

        Assert.False(result.AllSucceeded);
        Assert.Equal(0, result.Applied);
        Assert.NotNull(result.Error);
        Assert.Empty(fake.LoadCalls);
        Assert.Empty(store.All());
    }

    [Fact]
    public async Task ApplyOps_SnapshotsKnownGoodConfigOnce()
    {
        var (coord, fake, store) = NewCoordinator();
        var ops = new[] { Field(HandleArr + "/0/uri", "\"/z\"") };

        await coord.ApplyOpsAsync(ops, "ops");

        var snap = Assert.Single(store.All());
        // The snapshot is the PRE-edit config, not the candidate.
        Assert.Equal("/old", At(snap.ConfigJson, HandleArr + "/0/uri")!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyOps_ReadOnly_Blocks()
    {
        var (coord, fake, store) = NewCoordinator(readOnly: true);

        var result = await coord.ApplyOpsAsync(new[] { Field(HandleArr + "/0/uri", "\"/x\"") }, "ops");

        Assert.False(result.AllSucceeded);
        Assert.Equal(0, result.Applied);
        Assert.NotNull(result.Error);
        Assert.Empty(fake.LoadCalls);
        Assert.Empty(store.All());
    }

    [Fact]
    public async Task ApplyAsync_SingleRouteOp_GoesThroughLoad()
    {
        var (coord, fake, _) = NewCoordinator();

        var result = await coord.ApplyAsync(Field(HandleArr + "/0/uri", "\"/single\""), "single edit");

        Assert.True(result.Success);
        var candidate = Assert.Single(fake.LoadCalls);
        Assert.Equal("/single", At(candidate, HandleArr + "/0/uri")!.GetValue<string>());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }
}
