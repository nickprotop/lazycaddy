// -----------------------------------------------------------------------
// LazyCaddy - the single seam every write goes through:
// snapshot current config, run the write, return success/error.
// Dialogs/views call this; they never write directly.
// -----------------------------------------------------------------------

using LazyCaddy.Configuration;
using LazyCaddy.Models;

namespace LazyCaddy.Services;

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
