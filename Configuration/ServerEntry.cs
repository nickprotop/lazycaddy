// Configuration/ServerEntry.cs
using System.Text.Json.Serialization;

namespace LazyCaddy.Configuration;

/// <summary>One configured Caddy server: a display name, admin URL, and optional per-server
/// overrides. Identity is host:port (via LazyCaddyConfig.InstanceSlug) so the same endpoint under
/// http/https is one server and snapshots stay scoped correctly.</summary>
public sealed record ServerEntry(string Name, string Url)
{
    public string? CertDir { get; init; }
    public string? AccessLog { get; init; }
    public bool ReadOnly { get; init; }

    /// <summary>True for a transient --url entry that is shown but never written to servers.json.</summary>
    [JsonIgnore]
    public bool IsEphemeral { get; init; }

    /// <summary>Scheme-independent host:port identity key (reuses the snapshot-scoping slug).</summary>
    [JsonIgnore]
    public string Identity => LazyCaddyConfig.InstanceSlug(Url);
}
