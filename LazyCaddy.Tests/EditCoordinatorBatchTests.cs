using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class EditCoordinatorBatchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lazycaddy-batch-" + Guid.NewGuid().ToString("N"));

    /// <summary>Hand-written ICaddyAdmin fake: records Upsert calls, returns scripted results.</summary>
    private sealed class FakeAdmin : ICaddyAdmin
    {
        public string RawConfig = """{"config":"current"}""";
        public readonly List<(string Path, string Json)> UpsertCalls = new();
        public Queue<WriteResult> UpsertResults = new();

        public Task<string> GetRawConfigAsync(CancellationToken ct = default) => Task.FromResult(RawConfig);

        public Task<WriteResult> UpsertConfigAsync(string path, string json, CancellationToken ct = default)
        {
            UpsertCalls.Add((path, json));
            var r = UpsertResults.Count > 0 ? UpsertResults.Dequeue() : WriteResult.Ok;
            return Task.FromResult(r);
        }

        public Task<CaddyStatus> GetStatusAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Route>> GetRoutesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Cert>> GetCertsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Upstream>> GetUpstreamsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MetricsSnapshot> GetMetricsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetConfigNodeAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> PatchConfigAsync(string path, string json, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> PutConfigAsync(string path, string json, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> PostConfigAsync(string path, string json, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> DeleteConfigAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> LoadConfigAsync(string fullConfigJson, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private (EditCoordinator coord, FakeAdmin fake, SnapshotStore store) NewCoordinator(bool readOnly = false)
    {
        var fake = new FakeAdmin();
        var store = new SnapshotStore(_dir, 50);
        var config = LazyCaddyConfig.Default with { ReadOnly = readOnly };
        return (new EditCoordinator(fake, store, config), fake, store);
    }

    private static PendingWrite W(string path) => new(path, $$"""{"p":"{{path}}"}""", "{}", "label:" + path);

    [Fact]
    public async Task ApplyBatch_AllSucceed_WritesEachInOrder()
    {
        var (coord, fake, _) = NewCoordinator();
        var writes = new[] { W("a/0"), W("b/1"), W("c/2") };

        var result = await coord.ApplyBatchAsync(writes, "batch");

        Assert.Equal(3, result.Applied);
        Assert.Equal(3, result.Total);
        Assert.True(result.AllSucceeded);
        Assert.Null(result.Error);
        Assert.Equal(new[] { "a/0", "b/1", "c/2" }, fake.UpsertCalls.Select(c => c.Path).ToArray());
    }

    [Fact]
    public async Task ApplyBatch_StopsOnFirstFailure()
    {
        var (coord, fake, _) = NewCoordinator();
        fake.UpsertResults.Enqueue(WriteResult.Ok);          // write #1 ok
        fake.UpsertResults.Enqueue(WriteResult.Fail("bad")); // write #2 fails
        fake.UpsertResults.Enqueue(WriteResult.Ok);          // write #3 should never run
        var writes = new[] { W("a/0"), W("b/1"), W("c/2") };

        var result = await coord.ApplyBatchAsync(writes, "batch");

        Assert.Equal(1, result.Applied);
        Assert.Equal(3, result.Total);
        Assert.False(result.AllSucceeded);
        Assert.Equal(writes[1].Label, result.FailedLabel);
        Assert.NotNull(result.Error);
        Assert.Contains("bad", result.Error);
        // only writes 1 and 2 issued, not 3
        Assert.Equal(new[] { "a/0", "b/1" }, fake.UpsertCalls.Select(c => c.Path).ToArray());
    }

    [Fact]
    public async Task ApplyBatch_SnapshotsOnceBeforeWrites()
    {
        var (coord, _, store) = NewCoordinator();
        var writes = new[] { W("a/0"), W("b/1") };

        await coord.ApplyBatchAsync(writes, "batch");

        Assert.Single(store.All());
    }

    [Fact]
    public async Task ApplyBatch_Empty_NoSnapshotNoWrite()
    {
        var (coord, fake, store) = NewCoordinator();

        var result = await coord.ApplyBatchAsync(Array.Empty<PendingWrite>(), "batch");

        Assert.Equal(0, result.Applied);
        Assert.Equal(0, result.Total);
        Assert.True(result.AllSucceeded);
        Assert.Empty(fake.UpsertCalls);
        Assert.Empty(store.All());
    }

    [Fact]
    public async Task ApplyBatch_ReadOnly_BlocksWithoutSnapshotOrWrite()
    {
        var (coord, fake, store) = NewCoordinator(readOnly: true);

        var result = await coord.ApplyBatchAsync(new[] { W("a/0"), W("b/1") }, "batch");

        Assert.False(result.AllSucceeded);
        Assert.Equal(0, result.Applied);
        Assert.Equal(2, result.Total);
        Assert.NotNull(result.Error);
        Assert.Empty(fake.UpsertCalls);
        Assert.Empty(store.All());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }
}
