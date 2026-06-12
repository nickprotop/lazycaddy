using System.Text.Json.Nodes;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

/// <summary>
/// ApplyBatchAsync (the consolidated modal's PendingWrite batch) now computes a single candidate
/// config from all writes and POSTs /load once — transactional, all-or-nothing.
/// </summary>
public class EditCoordinatorBatchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lazycaddy-batch-" + Guid.NewGuid().ToString("N"));

    private const string BaseConfig = """
    {"apps":{"http":{"servers":{"srv0":{"routes":[
      {"match":[{"host":["a.example.com"]}],
       "handle":[{"handler":"reverse_proxy"}],
       "terminal":true}
    ]}}}}}
    """;

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

    private const string Route0 = "apps/http/servers/srv0/routes/0";

    // Each write upserts a field on route 0.
    private static PendingWrite W(string relPath, string json) =>
        new(Route0 + "/" + relPath, json, "{}", "label:" + relPath);

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

    [Fact]
    public async Task ApplyBatch_AllSucceed_SingleLoadWithAllFieldsApplied()
    {
        var (coord, fake, _) = NewCoordinator();
        var writes = new[]
        {
            W("terminal", "false"),
            W("match", """[{"host":["b.example.com"]}]"""),
        };

        var result = await coord.ApplyBatchAsync(writes, "batch");

        Assert.True(result.AllSucceeded);
        Assert.Equal(2, result.Applied);
        Assert.Equal(2, result.Total);
        var candidate = Assert.Single(fake.LoadCalls);
        Assert.False(At(candidate, Route0 + "/terminal")!.GetValue<bool>());
        Assert.Equal("b.example.com", At(candidate, Route0 + "/match/0/host/0")!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyBatch_LoadFails_AppliedZero_ErrorSurfaced()
    {
        var (coord, fake, store) = NewCoordinator();
        fake.LoadResults.Enqueue(WriteResult.Fail("bad config"));
        var writes = new[] { W("terminal", "false") };

        var result = await coord.ApplyBatchAsync(writes, "batch");

        Assert.False(result.AllSucceeded);
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Total);
        Assert.Contains("bad config", result.Error);
        Assert.Single(fake.LoadCalls);
        Assert.Single(store.All());
    }

    [Fact]
    public async Task ApplyBatch_InvalidWrite_NoLoad_NoSnapshot()
    {
        var (coord, fake, store) = NewCoordinator();
        // Path descends through a scalar (terminal) → candidate build fails.
        var writes = new[] { W("terminal/x", "1") };

        var result = await coord.ApplyBatchAsync(writes, "batch");

        Assert.False(result.AllSucceeded);
        Assert.Equal(0, result.Applied);
        Assert.NotNull(result.Error);
        Assert.Empty(fake.LoadCalls);
        Assert.Empty(store.All());
    }

    [Fact]
    public async Task ApplyBatch_SnapshotsKnownGoodOnce()
    {
        var (coord, _, store) = NewCoordinator();

        await coord.ApplyBatchAsync(new[] { W("terminal", "false") }, "batch");

        var snap = Assert.Single(store.All());
        Assert.True(At(snap.ConfigJson, Route0 + "/terminal")!.GetValue<bool>()); // pre-edit value
    }

    [Fact]
    public async Task ApplyBatch_Empty_NoSnapshotNoLoad()
    {
        var (coord, fake, store) = NewCoordinator();

        var result = await coord.ApplyBatchAsync(Array.Empty<PendingWrite>(), "batch");

        Assert.Equal(0, result.Applied);
        Assert.Equal(0, result.Total);
        Assert.True(result.AllSucceeded);
        Assert.Empty(fake.LoadCalls);
        Assert.Empty(store.All());
    }

    [Fact]
    public async Task ApplyBatch_ReadOnly_BlocksWithoutSnapshotOrLoad()
    {
        var (coord, fake, store) = NewCoordinator(readOnly: true);

        var result = await coord.ApplyBatchAsync(new[] { W("terminal", "false") }, "batch");

        Assert.False(result.AllSucceeded);
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Total);
        Assert.NotNull(result.Error);
        Assert.Empty(fake.LoadCalls);
        Assert.Empty(store.All());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }
}
