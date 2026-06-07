// -----------------------------------------------------------------------
// LazyCaddy - disk-persisted, capped, pinnable config snapshots.
// One JSON sidecar file per snapshot; survives restarts.
// -----------------------------------------------------------------------

using System.Text.Json;
using LazyCaddy.Models;

namespace LazyCaddy.Services;

public sealed class SnapshotStore
{
    private readonly string _dir;
    private readonly int _cap;
    private readonly List<Snapshot> _snaps = new();
    private int _seq;  // disambiguates captures within the same millisecond

    private sealed record Sidecar(DateTimeOffset TimestampUtc, string? Label, bool Pinned, string ConfigJson);

    public SnapshotStore(string dir, int maxAutoSnapshots)
    {
        _dir = dir;
        _cap = Math.Max(1, maxAutoSnapshots);
        Load();
    }

    /// <summary>All snapshots, newest first.</summary>
    public IReadOnlyList<Snapshot> All() =>
        _snaps.OrderByDescending(s => s.TimestampUtc).ThenByDescending(s => s.Id).ToList();

    public Snapshot Capture(string configJson, string? label)
    {
        Directory.CreateDirectory(_dir);
        var ts = DateTimeOffset.UtcNow;
        var id = $"{ts:yyyyMMdd-HHmmss-fff}-{_seq++:D4}";
        var path = Path.Combine(_dir, id + ".json");
        var snap = new Snapshot(id, ts, label, Pinned: false, configJson, path);
        WriteSidecar(snap);
        _snaps.Add(snap);
        Enforce();
        return snap;
    }

    public void Pin(string id, bool pinned)
    {
        var i = _snaps.FindIndex(s => s.Id == id);
        if (i < 0) return;
        _snaps[i] = _snaps[i] with { Pinned = pinned };
        WriteSidecar(_snaps[i]);
    }

    public void Delete(string id)
    {
        var i = _snaps.FindIndex(s => s.Id == id);
        if (i < 0) return;
        TryDeleteFile(_snaps[i].FilePath);
        _snaps.RemoveAt(i);
    }

    public Snapshot? MostRecent() => All().FirstOrDefault();

    private void Enforce()
    {
        var unpinned = _snaps.Where(s => !s.Pinned).OrderBy(s => s.TimestampUtc).ThenBy(s => s.Id).ToList();
        int over = unpinned.Count - _cap;
        for (int k = 0; k < over; k++)
        {
            var victim = unpinned[k];
            TryDeleteFile(victim.FilePath);
            _snaps.Remove(victim);
        }
    }

    private void Load()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                var sc = JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(file));
                if (sc is null) continue;
                var id = Path.GetFileNameWithoutExtension(file);
                _snaps.Add(new Snapshot(id, sc.TimestampUtc, sc.Label, sc.Pinned, sc.ConfigJson, file));
            }
            catch { /* skip corrupt sidecar */ }
        }
    }

    private void WriteSidecar(Snapshot s)
    {
        Directory.CreateDirectory(_dir);
        var sc = new Sidecar(s.TimestampUtc, s.Label, s.Pinned, s.ConfigJson);
        File.WriteAllText(s.FilePath, JsonSerializer.Serialize(sc));
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
