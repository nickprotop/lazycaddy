// -----------------------------------------------------------------------
// LazyCaddy - incremental file tail. Tracks a byte offset; each ReadNewLines()
// returns lines appended since the last read. Off-UI-thread only (does file I/O).
// Rotation/replacement: detects truncation (file shrank below offset) AND
// in-place overwrites (file was replaced with new content) by comparing a short
// fingerprint of the first bytes against what was seen after the previous read.
// A trailing partial line (no newline yet) is buffered until its newline arrives.
// Reads only the live path (no .gz archives).
// -----------------------------------------------------------------------

namespace LazyCaddy.Services;

public enum TailKind { Lines, NotFound, PermissionDenied }

public readonly record struct TailResult(TailKind Kind, IReadOnlyList<string> Lines)
{
    public static TailResult Of(IReadOnlyList<string> lines) => new(TailKind.Lines, lines);
    public static readonly TailResult NotFound = new(TailKind.NotFound, Array.Empty<string>());
    public static readonly TailResult PermissionDenied = new(TailKind.PermissionDenied, Array.Empty<string>());
}

public sealed class LogTailer
{
    private readonly string _path;
    private long _offset;
    private string _partial = "";
    // Fingerprint: first FingerprintLen bytes of the file as of the last read (or null if
    // offset was 0 / file was shorter). Used to detect in-place overwrites where the new
    // file is larger than the old offset so length-only comparison misses the replacement.
    private byte[]? _fingerprint;
    private const int FingerprintLen = 64;

    public LogTailer(string path) => _path = path;

    public TailResult ReadNewLines()
    {
        try
        {
            if (!File.Exists(_path)) { Reset(); return TailResult.NotFound; }

            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // Detect rotation/truncation: shrink OR in-place overwrite (same path, new content).
            bool rotated = fs.Length < _offset || HasFingerprintChanged(fs);
            if (rotated) Reset();

            // Capture fingerprint of the first bytes for future rotation detection.
            _fingerprint = ReadFingerprint(fs);

            if (fs.Length == _offset) return TailResult.Of(Array.Empty<string>());

            fs.Seek(_offset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            var chunk = reader.ReadToEnd();
            _offset = fs.Length;

            var combined = _partial + chunk;
            var lines = new List<string>();
            int start = 0, nl;
            while ((nl = combined.IndexOf('\n', start)) >= 0)
            {
                lines.Add(combined[start..nl].TrimEnd('\r'));
                start = nl + 1;
            }
            _partial = combined[start..];
            return TailResult.Of(lines);
        }
        catch (UnauthorizedAccessException) { return TailResult.PermissionDenied; }
        catch (IOException) { return TailResult.PermissionDenied; }
    }

    /// <summary>
    /// Returns true if the file's leading bytes differ from our stored fingerprint,
    /// indicating the file was replaced/rewritten since our last read.
    /// Only meaningful when _offset > 0 and we have a fingerprint to compare against.
    /// Compares only the bytes that were in the stored fingerprint (avoiding false positives
    /// when the file grows and ReadFingerprint would return more bytes than were stored).
    /// </summary>
    private bool HasFingerprintChanged(FileStream fs)
    {
        if (_fingerprint is null || _fingerprint.Length == 0 || _offset == 0 || fs.Length == 0) return false;
        // Read exactly as many bytes as stored in the fingerprint (or fewer if file is now shorter).
        int toRead = (int)Math.Min(_fingerprint.Length, fs.Length);
        var current = ReadExactBytes(fs, toRead);
        if (current.Length != toRead) return true;  // couldn't read enough → treat as rotated
        // Only compare the bytes we actually stored originally.
        return !current.AsSpan().SequenceEqual(_fingerprint.AsSpan()[..toRead]);
    }

    private static byte[] ReadExactBytes(FileStream fs, int count)
    {
        if (count <= 0) return Array.Empty<byte>();
        var buf = new byte[count];
        fs.Seek(0, SeekOrigin.Begin);
        int read = fs.Read(buf, 0, count);
        return read == count ? buf : buf[..read];
    }

    private static byte[] ReadFingerprint(FileStream fs)
    {
        if (fs.Length == 0) return Array.Empty<byte>();
        int toRead = (int)Math.Min(FingerprintLen, fs.Length);
        var buf = new byte[toRead];
        fs.Seek(0, SeekOrigin.Begin);
        int read = fs.Read(buf, 0, toRead);
        return read == toRead ? buf : buf[..read];
    }

    private void Reset() { _offset = 0; _partial = ""; _fingerprint = null; }
}
