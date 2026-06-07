using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class SnapshotStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lazycaddy-snap-" + Guid.NewGuid().ToString("N"));

    private SnapshotStore NewStore(int cap = 50) => new(_dir, cap);

    [Fact]
    public void Capture_PersistsToDisk_AndIsListed()
    {
        var s = NewStore();
        var snap = s.Capture("""{"a":1}""", label: "first");
        Assert.True(File.Exists(snap.FilePath));
        var all = s.All();
        Assert.Single(all);
        Assert.Equal("first", all[0].Label);
    }

    [Fact]
    public void Load_RehydratesFromDisk()
    {
        NewStore().Capture("""{"a":1}""", "x");
        var reopened = NewStore();   // fresh instance, same dir
        Assert.Single(reopened.All());
    }

    [Fact]
    public void Capture_DropsOldestUnpinned_OverCap()
    {
        var s = NewStore(cap: 2);
        var a = s.Capture("""{"n":1}""", null);
        s.Capture("""{"n":2}""", null);
        s.Capture("""{"n":3}""", null);   // exceeds cap -> oldest (a) dropped
        var all = s.All();
        Assert.Equal(2, all.Count);
        Assert.DoesNotContain(all, x => x.Id == a.Id);
        Assert.False(File.Exists(a.FilePath));
    }

    [Fact]
    public void Pin_PreventsAutoDrop_AndDoesNotCountAgainstCap()
    {
        var s = NewStore(cap: 2);
        var a = s.Capture("""{"n":1}""", null);
        s.Pin(a.Id, true);
        s.Capture("""{"n":2}""", null);
        s.Capture("""{"n":3}""", null);   // would drop oldest unpinned, not 'a'
        var all = s.All();
        Assert.Contains(all, x => x.Id == a.Id && x.Pinned);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
