// -----------------------------------------------------------------------
// LazyCaddy - one parsed access-log line. Built off the UI thread by the tail
// loop and only read on the UI thread. Raw is set (other fields default) for
// lines that aren't parseable Caddy JSON, so they're shown rather than dropped.
// -----------------------------------------------------------------------

namespace LazyCaddy.Models;

public sealed record AccessLogEntry(
    DateTimeOffset Time,
    int Status,
    string Method,
    string Host,
    string Uri,
    double DurationSeconds,
    long Size,
    string? Raw = null)
{
    /// <summary>True for a line that couldn't be parsed as a Caddy JSON access entry.</summary>
    public bool IsRaw => Raw is not null;
}
