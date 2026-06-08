// -----------------------------------------------------------------------
// LazyCaddy - the single seam every write goes through:
// snapshot current config, run the write, return success/error.
// Dialogs/views call this; they never write directly.
// -----------------------------------------------------------------------

using LazyCaddy.Configuration;
using LazyCaddy.Models;

namespace LazyCaddy.Services;

/// <summary>Outcome of a batched apply: how many writes landed, and the first failure (if any).</summary>
public readonly record struct BatchResult(int Applied, int Total, string? FailedLabel, string? Error)
{
    public bool AllSucceeded => Applied == Total && Error is null;
}

public sealed class EditCoordinator
{
    private readonly ICaddyAdmin _admin;
    private readonly SnapshotStore _snapshots;
    private readonly LazyCaddyConfig _config;

    public EditCoordinator(ICaddyAdmin admin, SnapshotStore snapshots, LazyCaddyConfig config)
    {
        _admin = admin; _snapshots = snapshots; _config = config;
    }

    public bool ReadOnly => _config.ReadOnly;

    public SnapshotStore Snapshots => _snapshots;

    /// <summary>GET a config node as raw JSON (passthrough for dialogs that diff before patching).</summary>
    public Task<string> GetConfigNodeAsync(string path, CancellationToken ct = default)
        => _admin.GetConfigNodeAsync(path, ct);

    /// <summary>GET the full running config as raw JSON (passthrough for manual snapshots).</summary>
    public Task<string> GetRawConfigAsync(CancellationToken ct = default)
        => _admin.GetRawConfigAsync(ct);

    /// <summary>Snapshot the current full config, then run <paramref name="write"/>.</summary>
    public async Task<WriteResult> ApplyAsync(
        Func<ICaddyAdmin, CancellationToken, Task<WriteResult>> write,
        string? snapshotLabel, CancellationToken ct = default)
    {
        if (_config.ReadOnly) return WriteResult.Fail("Editing is disabled (read-only mode).");
        try
        {
            var current = await _admin.GetRawConfigAsync(ct).ConfigureAwait(false);
            _snapshots.Capture(current, snapshotLabel);
        }
        catch (Exception ex)
        {
            return WriteResult.Fail($"Could not snapshot before edit: {ex.Message}");
        }
        return await write(_admin, ct).ConfigureAwait(false);
    }

    /// <summary>Snapshot the current config ONCE, then apply each write via Upsert in order, stopping
    /// at the first failure (prior writes stay applied). For the consolidated modal's single Apply.</summary>
    public async Task<BatchResult> ApplyBatchAsync(IReadOnlyList<PendingWrite> writes, string label, CancellationToken ct = default)
    {
        if (_config.ReadOnly) return new BatchResult(0, writes.Count, null, "Editing is disabled (read-only mode).");
        if (writes.Count == 0) return new BatchResult(0, 0, null, null);
        try
        {
            var current = await _admin.GetRawConfigAsync(ct).ConfigureAwait(false);
            _snapshots.Capture(current, label);
        }
        catch (Exception ex)
        {
            return new BatchResult(0, writes.Count, null, $"Could not snapshot before edit: {ex.Message}");
        }

        int applied = 0;
        foreach (var w in writes)
        {
            var r = await _admin.UpsertConfigAsync(w.Path, w.Json, ct).ConfigureAwait(false);
            if (!r.Success) return new BatchResult(applied, writes.Count, w.Label, r.Error ?? "write failed");
            applied++;
        }
        return new BatchResult(applied, writes.Count, null, null);
    }

    /// <summary>Snapshot once, then apply a mixed op batch in a hazard-safe order: deletes
    /// (high array-index first, so earlier deletes don't shift later ones), then adds (POST-append),
    /// then field upserts (re-pathed for any lower-index delete in the same array that shifted them).
    /// Stops at the first failure.
    ///
    /// Re-pathing limitation: it handles single-array index shifts for the handle[] arrays that
    /// add/delete touch. A field whose OWN handler is deleted must not be emitted by the modal
    /// (out of scope here). Multiple deletes in the SAME array at different indices decrement
    /// cumulatively.</summary>
    public async Task<BatchResult> ApplyOpsAsync(IReadOnlyList<RouteOp> ops, string label, CancellationToken ct = default)
    {
        if (_config.ReadOnly) return new BatchResult(0, ops.Count, null, "Editing is disabled (read-only mode).");
        if (ops.Count == 0) return new BatchResult(0, 0, null, null);
        try
        {
            var current = await _admin.GetRawConfigAsync(ct).ConfigureAwait(false);
            _snapshots.Capture(current, label);
        }
        catch (Exception ex)
        {
            return new BatchResult(0, ops.Count, null, $"Could not snapshot before edit: {ex.Message}");
        }

        var deletes = ops.Where(o => o.Kind == RouteOpKind.Delete)
            .OrderByDescending(o => ArrayIndexOf(o.Path)).ToList();
        var adds = ops.Where(o => o.Kind == RouteOpKind.Add).ToList();
        var fields = ops.Where(o => o.Kind == RouteOpKind.Field).ToList();

        int applied = 0;
        var appliedDeletes = new List<(string arr, int idx)>();
        foreach (var d in deletes)
        {
            var r = await _admin.DeleteConfigAsync(d.Path, ct).ConfigureAwait(false);
            if (!r.Success) return new BatchResult(applied, ops.Count, d.Label, r.Error ?? "delete failed");
            applied++; appliedDeletes.Add((ArrayOf(d.Path), ArrayIndexOf(d.Path)));
        }
        foreach (var a in adds)
        {
            var r = await _admin.PostConfigAsync(a.Path, a.Json, ct).ConfigureAwait(false);
            if (!r.Success) return new BatchResult(applied, ops.Count, a.Label, r.Error ?? "add failed");
            applied++;
        }
        foreach (var f in fields)
        {
            var path = RepathAfterDeletes(f.Path, appliedDeletes);
            var r = await _admin.UpsertConfigAsync(path, f.Json, ct).ConfigureAwait(false);
            if (!r.Success) return new BatchResult(applied, ops.Count, f.Label, r.Error ?? "write failed");
            applied++;
        }
        return new BatchResult(applied, ops.Count, null, null);
    }

    // "a/b/handle/3" → array path "a/b/handle", index 3. For a non-element path, index -1.
    internal static string ArrayOf(string nodePath) { var i = nodePath.LastIndexOf('/'); return i < 0 ? nodePath : nodePath[..i]; }
    internal static int ArrayIndexOf(string nodePath) { var seg = nodePath[(nodePath.LastIndexOf('/') + 1)..]; return int.TryParse(seg, out var n) ? n : -1; }

    /// <summary>Adjust a field path for index shifts caused by deletes in the same array(s):
    /// for each delete at (arr, di), any field path segment "{arr}/{fi}" with fi > di shifts to fi-1.</summary>
    internal static string RepathAfterDeletes(string fieldPath, IReadOnlyList<(string arr, int idx)> deletes)
    {
        if (deletes.Count == 0) return fieldPath;
        // Re-path each "{arr}/{index}/..." occurrence. Sort deletes ascending so cumulative decrements are correct.
        foreach (var (arr, di) in deletes.OrderBy(d => d.idx))
        {
            if (di < 0) continue; // non-array-element delete (no numeric index) → can't shift field indices
            var prefix = arr + "/";
            if (!fieldPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = fieldPath[prefix.Length..];                 // "{fi}/..." or "{fi}"
            var slash = rest.IndexOf('/');
            var idxSeg = slash < 0 ? rest : rest[..slash];
            if (!int.TryParse(idxSeg, out var fi)) continue;
            if (fi > di) { fi--; fieldPath = prefix + fi + (slash < 0 ? "" : rest[slash..]); }
        }
        return fieldPath;
    }

    /// <summary>Restore a snapshot's config via POST /load (snapshotting current first).</summary>
    public async Task<WriteResult> RestoreAsync(Snapshot snap, CancellationToken ct = default)
    {
        if (_config.ReadOnly) return WriteResult.Fail("Editing is disabled (read-only mode).");
        try
        {
            var current = await _admin.GetRawConfigAsync(ct).ConfigureAwait(false);
            _snapshots.Capture(current, $"before restore of {snap.Id}");
        }
        catch (Exception ex)
        {
            return WriteResult.Fail($"Could not snapshot before restore: {ex.Message}");
        }
        return await _admin.LoadConfigAsync(snap.ConfigJson, ct).ConfigureAwait(false);
    }
}
