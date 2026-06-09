using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class LogTailerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "lc-tail-" + Guid.NewGuid().ToString("N") + ".log");

    private void Append(string text) => File.AppendAllText(_path, text);

    [Fact]
    public void Read_NewlyAppendedLines_ReturnedOnce()
    {
        File.WriteAllText(_path, "line1\nline2\n");
        var t = new LogTailer(_path);

        var r1 = t.ReadNewLines();
        Assert.Equal(TailKind.Lines, r1.Kind);
        Assert.Equal(new[] { "line1", "line2" }, r1.Lines);

        var r2 = t.ReadNewLines();
        Assert.Equal(TailKind.Lines, r2.Kind);
        Assert.Empty(r2.Lines);

        Append("line3\n");
        var r3 = t.ReadNewLines();
        Assert.Equal(new[] { "line3" }, r3.Lines);
    }

    [Fact]
    public void Read_PartialLine_BufferedUntilNewline()
    {
        File.WriteAllText(_path, "comp");
        var t = new LogTailer(_path);
        Assert.Empty(t.ReadNewLines().Lines);
        Append("lete\n");
        Assert.Equal(new[] { "complete" }, t.ReadNewLines().Lines);
    }

    [Fact]
    public void Read_Truncation_ResetsAndRereads()
    {
        File.WriteAllText(_path, "a\nb\n");
        var t = new LogTailer(_path);
        t.ReadNewLines();

        File.WriteAllText(_path, "fresh\n");
        var r = t.ReadNewLines();
        Assert.Equal(new[] { "fresh" }, r.Lines);
    }

    [Fact]
    public void Read_MissingFile_ReturnsNotFound()
    {
        var t = new LogTailer(Path.Combine(Path.GetTempPath(), "lc-nope-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(TailKind.NotFound, t.ReadNewLines().Kind);
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }
}
