// -----------------------------------------------------------------------
// LazyCaddy - thread-safe hand-off between the background tail loop (writer)
// and the LogsView on the UI thread (reader). The tail loop appends parsed
// entries + sets the current source/status; the view drains new entries each
// tick. IsActive gates whether the tail loop does any I/O at all.
// -----------------------------------------------------------------------

using LazyCaddy.Models;

namespace LazyCaddy.Services;

public sealed class LogState
{
    private readonly object _lock = new();
    private readonly Queue<AccessLogEntry> _pending = new();
    private const int PendingCap = 2000;

    /// <summary>Set by the view (on Build / nav change). The tail loop only reads while true.</summary>
    public volatile bool IsActive;

    /// <summary>Latest source/status for the banner (set by the tail loop).</summary>
    public LogSource Source { get; set; } = LogSource.NotConfigured;
    public TailKind LastTail { get; set; } = TailKind.Lines;

    public void Append(IEnumerable<AccessLogEntry> entries)
    {
        lock (_lock)
        {
            foreach (var e in entries)
            {
                _pending.Enqueue(e);
                while (_pending.Count > PendingCap) _pending.Dequeue();
            }
        }
    }

    /// <summary>Remove and return everything appended since the last drain (UI thread).</summary>
    public IReadOnlyList<AccessLogEntry> Drain()
    {
        lock (_lock)
        {
            if (_pending.Count == 0) return Array.Empty<AccessLogEntry>();
            var list = _pending.ToArray();
            _pending.Clear();
            return list;
        }
    }
}
