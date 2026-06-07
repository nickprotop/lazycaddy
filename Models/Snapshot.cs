// -----------------------------------------------------------------------
// LazyCaddy - a point-in-time capture of the full Caddy config.
// -----------------------------------------------------------------------

namespace LazyCaddy.Models;

public sealed record Snapshot(
    string Id,                 // filename stem, sortable: yyyyMMdd-HHmmss-fff-NNNN
    DateTimeOffset TimestampUtc,
    string? Label,
    bool Pinned,
    string ConfigJson,
    string FilePath);
