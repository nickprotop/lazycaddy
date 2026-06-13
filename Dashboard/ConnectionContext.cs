// Dashboard/ConnectionContext.cs
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.Dashboard;

/// <summary>The full per-server dependency bundle the shell points at. Switching servers swaps the
/// active ConnectionContext; the Generation lets the poll loop discard results from a prior server.</summary>
public sealed class ConnectionContext : IDisposable
{
    public required LazyCaddyConfig Config { get; init; }
    public required CaddyAdminClient Admin { get; init; }
    public required UpstreamProber Prober { get; init; }
    public required SnapshotStore Snapshots { get; init; }
    public required EditCoordinator Editor { get; init; }
    public required ServerEntry Server { get; init; }
    public required int Generation { get; init; }

    /// <summary>Build a context for a server entry. snapshotRoot is the base snapshots dir
    /// (the instance subdir is derived from the URL); generation is the monotonically-increasing
    /// switch counter.</summary>
    public static ConnectionContext Create(ServerEntry entry, string snapshotRoot, int generation, bool simulateDown = false)
    {
        var config = LazyCaddyConfig.Default with
        {
            AdminApiUrl = entry.Url,
            ReadOnly = entry.ReadOnly,
            SnapshotDir = snapshotRoot,
        };
        if (entry.CertDir is not null) config = config with { CaddyDataDir = entry.CertDir };
        if (entry.AccessLog is not null) config = config with { AccessLogPath = entry.AccessLog };

        var admin = new CaddyAdminClient(config, simulateDisconnected: simulateDown);
        var prober = new UpstreamProber(config);
        var snapshots = new SnapshotStore(config.InstanceSnapshotDir, config.MaxAutoSnapshots);
        var editor = new EditCoordinator(admin, snapshots, config);
        return new ConnectionContext
        {
            Config = config, Admin = admin, Prober = prober,
            Snapshots = snapshots, Editor = editor, Server = entry, Generation = generation,
        };
    }

    public void Dispose() => Admin.Dispose();
}
