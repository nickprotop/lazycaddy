// Services/ServerSelection.cs
using LazyCaddy.Configuration;

namespace LazyCaddy.Services;

/// <summary>The effective server list for a run plus which one is active at startup.</summary>
public readonly record struct ServerSelectionResult(IReadOnlyList<ServerEntry> Servers, int ActiveIndex);

/// <summary>Pure startup resolver: merges the optional --url override into the configured list.
/// --url matching a configured server selects it; otherwise it's added as an ephemeral entry and
/// selected; no --url selects the first configured server.</summary>
public static class ServerSelection
{
    public static ServerSelectionResult Resolve(string? cliUrl, IReadOnlyList<ServerEntry> configured)
    {
        var list = configured.ToList();
        if (string.IsNullOrWhiteSpace(cliUrl))
            return new ServerSelectionResult(list, list.Count > 0 ? 0 : 0);

        var id = LazyCaddyConfig.InstanceSlug(cliUrl);
        int match = list.FindIndex(s => s.Identity == id);
        if (match >= 0)
            return new ServerSelectionResult(list, match);

        list.Add(new ServerEntry("(cli)", cliUrl.Trim()) { IsEphemeral = true });
        return new ServerSelectionResult(list, list.Count - 1);
    }
}
