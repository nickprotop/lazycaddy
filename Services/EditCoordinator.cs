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

    /// <summary>Adapt a Caddyfile to JSON (passthrough; does not change the running config).</summary>
    public Task<AdaptResult> AdaptCaddyfileAsync(string caddyfile, CancellationToken ct = default)
        => _admin.AdaptCaddyfileAsync(caddyfile, ct);

    /// <summary>Snapshot the current config, then replace it wholesale via POST /load.</summary>
    public async Task<WriteResult> LoadFullConfigAsync(string fullConfigJson, string snapshotLabel, CancellationToken ct = default)
    {
        if (_config.ReadOnly) return WriteResult.Fail("Editing is disabled (read-only mode).");
        try
        {
            var current = await _admin.GetRawConfigAsync(ct).ConfigureAwait(false);
            _snapshots.Capture(current, snapshotLabel);
        }
        catch (Exception ex)
        {
            return WriteResult.Fail($"Could not snapshot before load: {ex.Message}");
        }
        return await _admin.LoadConfigAsync(fullConfigJson, ct).ConfigureAwait(false);
    }

    /// <summary>Apply a single route op transactionally (compute candidate → atomic /load).</summary>
    public async Task<WriteResult> ApplyAsync(RouteOp op, string? snapshotLabel, CancellationToken ct = default)
    {
        var r = await ApplyCandidateAsync(new[] { op }, snapshotLabel, ct).ConfigureAwait(false);
        return r.AllSucceeded ? WriteResult.Ok : WriteResult.Fail(r.Error ?? "write failed");
    }

    /// <summary>Apply the consolidated modal's PendingWrite batch transactionally: every write is a
    /// Field (upsert) op folded into one candidate config, applied via a single atomic /load.</summary>
    public Task<BatchResult> ApplyBatchAsync(IReadOnlyList<PendingWrite> writes, string label, CancellationToken ct = default)
        => ApplyCandidateAsync(writes.Select(RouteOp.Field).ToList(), label, ct);

    /// <summary>Apply a mixed route-op batch transactionally. The candidate is computed in-memory
    /// (deletes/adds/fields applied to a live tree, so no array-index re-pathing is needed), then
    /// POSTed via a single /load. Caddy's /load is all-or-nothing: the whole batch lands or the
    /// whole config rolls back — there is no partial state.</summary>
    public Task<BatchResult> ApplyOpsAsync(IReadOnlyList<RouteOp> ops, string label, CancellationToken ct = default)
        => ApplyCandidateAsync(ops, label, ct);

    /// <summary>The single seam: read current config, compute the candidate, snapshot the
    /// known-good config, then POST /load the candidate once. A build error (bad path/fragment)
    /// short-circuits before any snapshot or network call, so nothing is applied.</summary>
    private async Task<BatchResult> ApplyCandidateAsync(IReadOnlyList<RouteOp> ops, string? label, CancellationToken ct)
    {
        if (_config.ReadOnly) return new BatchResult(0, ops.Count, null, "Editing is disabled (read-only mode).");
        if (ops.Count == 0) return new BatchResult(0, 0, null, null);

        string current;
        try
        {
            current = await _admin.GetRawConfigAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new BatchResult(0, ops.Count, null, $"Could not read config before edit: {ex.Message}");
        }

        string candidate;
        try
        {
            candidate = ConfigCandidateBuilder.Apply(current, ops);
        }
        catch (CandidateBuildException ex)
        {
            // Nothing applied: no snapshot, no /load.
            return new BatchResult(0, ops.Count, null, ex.Message);
        }

        _snapshots.Capture(current, label); // snapshot the known-good config (not the candidate)

        var r = await _admin.LoadConfigAsync(candidate, ct).ConfigureAwait(false);
        return r.Success
            ? new BatchResult(ops.Count, ops.Count, null, null)
            : new BatchResult(0, ops.Count, null, r.Error ?? "load failed");
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
