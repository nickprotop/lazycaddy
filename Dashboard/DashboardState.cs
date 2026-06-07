// -----------------------------------------------------------------------
// LazyCaddy - shared, thread-safe holder for the latest poll result.
//
// The background poll thread writes a fresh CaddySnapshot here; the UI thread
// reads it (inside an EnqueueOnUIThread action) to refresh the views. A single
// reference swap under lock keeps reads/writes consistent without tearing.
// -----------------------------------------------------------------------

using LazyCaddy.Models;

namespace LazyCaddy.Dashboard;

public enum ConnectionState { Connecting, Connected, Disconnected }

public sealed class DashboardState
{
    private readonly object _gate = new();
    private CaddySnapshot? _snapshot;
    private ConnectionState _connection = ConnectionState.Connecting;
    private string? _lastError;

    /// <summary>Latest successful snapshot, or null before the first success.</summary>
    public CaddySnapshot? Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public ConnectionState Connection
    {
        get { lock (_gate) return _connection; }
    }

    public string? LastError
    {
        get { lock (_gate) return _lastError; }
    }

    public void SetConnecting()
    {
        lock (_gate) _connection = ConnectionState.Connecting;
    }

    public void SetConnected(CaddySnapshot snapshot)
    {
        lock (_gate)
        {
            _snapshot = snapshot;
            _connection = ConnectionState.Connected;
            _lastError = null;
        }
    }

    public void SetDisconnected(string error)
    {
        lock (_gate)
        {
            _connection = ConnectionState.Disconnected;
            _lastError = error;
        }
    }
}
