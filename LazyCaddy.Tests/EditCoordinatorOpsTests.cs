using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class EditCoordinatorOpsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lazycaddy-ops-" + Guid.NewGuid().ToString("N"));

    /// <summary>Hand-written ICaddyAdmin fake: records ordered (method, path) calls,
    /// returns scripted results per method (default Ok).</summary>
    private sealed class FakeAdmin : ICaddyAdmin
    {
        public string RawConfig = """{"config":"current"}""";
        public readonly List<(string Method, string Path)> Calls = new();
        public Queue<WriteResult> UpsertResults = new();
        public Queue<WriteResult> PostResults = new();
        public Queue<WriteResult> DeleteResults = new();

        public Task<string> GetRawConfigAsync(CancellationToken ct = default) => Task.FromResult(RawConfig);

        public Task<WriteResult> UpsertConfigAsync(string path, string json, CancellationToken ct = default)
        {
            Calls.Add(("UPSERT", path));
            return Task.FromResult(UpsertResults.Count > 0 ? UpsertResults.Dequeue() : WriteResult.Ok);
        }

        public Task<WriteResult> PostConfigAsync(string path, string json, CancellationToken ct = default)
        {
            Calls.Add(("POST", path));
            return Task.FromResult(PostResults.Count > 0 ? PostResults.Dequeue() : WriteResult.Ok);
        }

        public Task<WriteResult> DeleteConfigAsync(string path, CancellationToken ct = default)
        {
            Calls.Add(("DELETE", path));
            return Task.FromResult(DeleteResults.Count > 0 ? DeleteResults.Dequeue() : WriteResult.Ok);
        }

        public Task<CaddyStatus> GetStatusAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Route>> GetRoutesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Cert>> GetCertsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Upstream>> GetUpstreamsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MetricsSnapshot> GetMetricsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetConfigNodeAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> PatchConfigAsync(string path, string json, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> PutConfigAsync(string path, string json, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WriteResult> LoadConfigAsync(string fullConfigJson, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private (EditCoordinator coord, FakeAdmin fake, SnapshotStore store) NewCoordinator(bool readOnly = false)
    {
        var fake = new FakeAdmin();
        var store = new SnapshotStore(_dir, 50);
        var config = LazyCaddyConfig.Default with { ReadOnly = readOnly };
        return (new EditCoordinator(fake, store, config), fake, store);
    }

    private static RouteOp Field(string path) =>
        RouteOp.Field(new PendingWrite(path, $$"""{"p":"{{path}}"}""", "{}", "field:" + path));

    [Fact]
    public async Task ApplyOps_Empty_NoSnapshotNoWrite()
    {
        var (coord, fake, store) = NewCoordinator();

        var result = await coord.ApplyOpsAsync(Array.Empty<RouteOp>(), "ops");

        Assert.Equal(0, result.Applied);
        Assert.Equal(0, result.Total);
        Assert.Null(result.FailedLabel);
        Assert.Null(result.Error);
        Assert.Empty(fake.Calls);
        Assert.Empty(store.All());
    }

    [Fact]
    public async Task ApplyOps_OrdersDeletesHighIndexFirst_ThenAdds_ThenFields()
    {
        var (coord, fake, _) = NewCoordinator();
        var ops = new[]
        {
            RouteOp.Delete("a/handle/0", "{}", "del0"),
            Field("a/handle/1/uri"),
            RouteOp.Add("a/handle", """{"handler":"x"}""", "add"),
            RouteOp.Delete("a/handle/2", "{}", "del2"),
        };

        var result = await coord.ApplyOpsAsync(ops, "ops");

        Assert.True(result.AllSucceeded);
        Assert.Equal(4, result.Applied);
        Assert.Equal(4, result.Total);
        // deletes by descending index, then add, then field.
        // del0 deletes index 0 => the field's handler shifts from index 1 to index 0.
        Assert.Equal(new[]
        {
            ("DELETE", "a/handle/2"),
            ("DELETE", "a/handle/0"),
            ("POST", "a/handle"),
            ("UPSERT", "a/handle/0/uri"),
        }, fake.Calls.ToArray());
    }

    [Fact]
    public async Task ApplyOps_StopsOnFirstFailure()
    {
        var (coord, fake, _) = NewCoordinator();
        fake.DeleteResults.Enqueue(WriteResult.Fail("boom")); // first delete (highest index) fails
        var ops = new[]
        {
            RouteOp.Delete("a/handle/0", "{}", "del0"),
            RouteOp.Delete("a/handle/2", "{}", "del2"),
            RouteOp.Add("a/handle", """{"handler":"x"}""", "add"),
        };

        var result = await coord.ApplyOpsAsync(ops, "ops");

        Assert.False(result.AllSucceeded);
        Assert.Equal(0, result.Applied);
        Assert.Equal(3, result.Total);
        Assert.Equal("del2", result.FailedLabel); // highest index goes first
        Assert.NotNull(result.Error);
        Assert.Contains("boom", result.Error);
        // only the failing delete was issued, nothing after
        Assert.Equal(new[] { ("DELETE", "a/handle/2") }, fake.Calls.ToArray());
    }

    [Fact]
    public async Task ApplyOps_SnapshotsOnce()
    {
        var (coord, _, store) = NewCoordinator();
        var ops = new[]
        {
            RouteOp.Delete("a/handle/0", "{}", "del0"),
            RouteOp.Add("a/handle", """{"handler":"x"}""", "add"),
            Field("a/handle/3/uri"),
        };

        await coord.ApplyOpsAsync(ops, "ops");

        Assert.Single(store.All());
    }

    [Fact]
    public async Task ApplyOps_FieldRepathedAfterLowerDelete()
    {
        var (coord, fake, _) = NewCoordinator();
        var ops = new[]
        {
            RouteOp.Delete("a/handle/0", "{}", "del0"),
            Field("a/handle/2/uri"),
        };

        var result = await coord.ApplyOpsAsync(ops, "ops");

        Assert.True(result.AllSucceeded);
        // index 0 deleted => field's handler shifted 2 -> 1
        var upsert = Assert.Single(fake.Calls, c => c.Method == "UPSERT");
        Assert.Equal("a/handle/1/uri", upsert.Path);
    }

    [Fact]
    public async Task ApplyOps_FieldRepathedAfterTwoLowerDeletes_EndToEnd()
    {
        // End-to-end (not just the helper): two deletes below the field shift it cumulatively.
        var (coord, fake, _) = NewCoordinator();
        var ops = new[]
        {
            RouteOp.Delete("a/handle/0", "{}", "del0"),
            RouteOp.Delete("a/handle/1", "{}", "del1"),
            Field("a/handle/3/uri"),
        };

        var result = await coord.ApplyOpsAsync(ops, "ops");

        Assert.True(result.AllSucceeded);
        // deletes applied high-first (DELETE 1, DELETE 0); field at index 3 shifts down by 2 -> 1.
        Assert.Equal(new[] { ("DELETE", "a/handle/1"), ("DELETE", "a/handle/0") },
            fake.Calls.Where(c => c.Method == "DELETE").ToArray());
        var upsert = Assert.Single(fake.Calls, c => c.Method == "UPSERT");
        Assert.Equal("a/handle/1/uri", upsert.Path);
    }

    [Fact]
    public void Repath_NonArrayElementDelete_DoesNotShift()
    {
        // A delete whose path has no numeric index (di == -1) must not spuriously shift field indices.
        var result = EditCoordinator.RepathAfterDeletes("a/handle/2/uri", new[] { ("a/handle", -1) });
        Assert.Equal("a/handle/2/uri", result);
    }

    [Fact]
    public async Task ApplyOps_ReadOnly_Blocks()
    {
        var (coord, fake, store) = NewCoordinator(readOnly: true);

        var result = await coord.ApplyOpsAsync(new[] { Field("a/handle/0/uri") }, "ops");

        Assert.False(result.AllSucceeded);
        Assert.Equal(0, result.Applied);
        Assert.NotNull(result.Error);
        Assert.Empty(fake.Calls);
        Assert.Empty(store.All());
    }

    // --- Direct unit tests of the re-pathing helper (internal via InternalsVisibleTo) ---

    [Fact]
    public void Repath_FieldAboveDelete_Decrements()
        => Assert.Equal("a/handle/1/uri",
            EditCoordinator.RepathAfterDeletes("a/handle/2/uri", new[] { ("a/handle", 0) }));

    [Fact]
    public void Repath_FieldInDifferentArray_Unchanged()
        => Assert.Equal("b/handle/2/uri",
            EditCoordinator.RepathAfterDeletes("b/handle/2/uri", new[] { ("a/handle", 0) }));

    [Fact]
    public void Repath_FieldAtOrBelowDeleteIndex_Unchanged()
    {
        Assert.Equal("a/handle/2/uri",
            EditCoordinator.RepathAfterDeletes("a/handle/2/uri", new[] { ("a/handle", 3) }));
        // equal index => not greater than, so unchanged
        Assert.Equal("a/handle/2/uri",
            EditCoordinator.RepathAfterDeletes("a/handle/2/uri", new[] { ("a/handle", 2) }));
    }

    [Fact]
    public void Repath_MultipleLowerDeletes_DecrementCumulatively()
        => Assert.Equal("a/handle/3/uri",
            EditCoordinator.RepathAfterDeletes("a/handle/5/uri", new[] { ("a/handle", 0), ("a/handle", 1) }));

    [Fact]
    public void Repath_NoDeletes_Unchanged()
        => Assert.Equal("a/handle/2/uri",
            EditCoordinator.RepathAfterDeletes("a/handle/2/uri", Array.Empty<(string, int)>()));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }
}
