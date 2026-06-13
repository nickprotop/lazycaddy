// Services/ServerStore.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using LazyCaddy.Configuration;

namespace LazyCaddy.Services;

/// <summary>Outcome of loading servers.json: the effective list plus flags for UI warnings.</summary>
public readonly record struct ServerLoadResult(
    IReadOnlyList<ServerEntry> Servers, bool Malformed, bool HadDuplicates);

/// <summary>Pure persistence for the configured server list (no HTTP/UI). Missing file → one
/// implicit local default; malformed → default + Malformed flag; duplicate host:port identities
/// are de-duped (first wins) with HadDuplicates set.</summary>
public sealed class ServerStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        // Write camelCase keys ("name"/"url"/"readOnly") so the file matches the hand-written style;
        // reads stay case-insensitive so either casing loads.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ServerStore(string path) { _path = path; }

    public static ServerEntry LocalDefault => new("local", "http://localhost:2019");

    private sealed class FileShape { public List<ServerEntry> Servers { get; set; } = new(); }

    public ServerLoadResult Load()
    {
        if (!File.Exists(_path))
            return new ServerLoadResult(new[] { LocalDefault }, Malformed: false, HadDuplicates: false);

        FileShape? parsed;
        try { parsed = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), Json); }
        catch { return new ServerLoadResult(new[] { LocalDefault }, Malformed: true, HadDuplicates: false); }

        var raw = parsed?.Servers ?? new List<ServerEntry>();
        if (raw.Count == 0)
            return new ServerLoadResult(new[] { LocalDefault }, Malformed: false, HadDuplicates: false);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<ServerEntry>();
        bool dupes = false;
        foreach (var s in raw)
        {
            if (seen.Add(s.Identity)) deduped.Add(s);
            else dupes = true;
        }
        return new ServerLoadResult(deduped, Malformed: false, HadDuplicates: dupes);
    }

    /// <summary>Atomically persist the non-ephemeral servers (temp write + rename).</summary>
    public void Save(IReadOnlyList<ServerEntry> servers)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var persistable = servers.Where(s => !s.IsEphemeral).ToList();
        var json = JsonSerializer.Serialize(new FileShape { Servers = persistable }, Json);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    /// <summary>Returns an error message if the entry is invalid against the existing list, else null.</summary>
    public static string? Validate(ServerEntry candidate, IReadOnlyList<ServerEntry> existing)
    {
        if (string.IsNullOrWhiteSpace(candidate.Name)) return "Name is required.";
        if (!Uri.TryCreate(candidate.Url?.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return "URL must be an absolute http(s) URL.";
        if (existing.Any(e => string.Equals(e.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)))
            return $"A server named '{candidate.Name}' already exists.";
        return null;
    }
}
