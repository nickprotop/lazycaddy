// -----------------------------------------------------------------------
// LazyCaddy - builds the shell-owned palette commands: one Navigation command
// per view (jump there) + the Global actions (refresh, undo, snapshot, adapt,
// help, quit). Takes view descriptors + action delegates so it stays pure and
// unit-testable; the shell supplies the real delegates.
// -----------------------------------------------------------------------

namespace LazyCaddy.Services;

/// <summary>One navigable view: its nav index (1-based), display label, and icon.</summary>
public readonly record struct ViewDescriptor(int Index, string Label, string Icon);

public static class CommandCatalog
{
    /// <summary>The shell behaviours the catalog's commands invoke.</summary>
    public readonly record struct Actions(
        Action<int> GoToView,
        Action Refresh,
        Action QuickUndo,
        Action SnapshotNow,
        Action AdaptCaddyfile,
        Action ShowHelp,
        Action Quit);

    public static List<Command> Build(IReadOnlyList<ViewDescriptor> views, Actions a)
    {
        var cmds = new List<Command>();

        // Navigation: jump to each view. "go.<slug>" ids, highest-priority group so a blank
        // palette leads with navigation.
        foreach (var v in views)
        {
            var idx = v.Index;
            cmds.Add(new Command
            {
                Id = $"go.{Slug(v.Label)}",
                Label = $"Go to {v.Label}",
                Category = "Go",
                Icon = v.Icon,
                Keybinding = idx.ToString(),
                Priority = 100,
                Execute = _ => a.GoToView(idx),
            });
        }

        // Global actions.
        cmds.Add(new Command
        {
            Id = "global.refresh", Label = "Refresh", Category = "Global", Icon = "↻",
            Keybinding = "R", Priority = 90, Execute = _ => a.Refresh(),
        });
        cmds.Add(new Command
        {
            Id = "global.undo", Label = "Undo (restore last snapshot)", Category = "Global", Icon = "⤺",
            Keybinding = "U", Priority = 80,
            CanExecute = ctx => ctx.HasSnapshots,
            DisabledReason = _ => "no snapshots yet",
            Execute = _ => a.QuickUndo(),
        });
        cmds.Add(new Command
        {
            Id = "global.snapshot", Label = "Snapshot now", Category = "Global", Icon = "⟲",
            Keybinding = "Shift+S", Priority = 75, Execute = _ => a.SnapshotNow(),
        });
        cmds.Add(new Command
        {
            Id = "global.adapt", Label = "Adapt Caddyfile → JSON", Category = "Global", Icon = "⇄",
            Priority = 60, Execute = _ => a.AdaptCaddyfile(),
        });
        cmds.Add(new Command
        {
            Id = "global.help", Label = "Show Help", Category = "Global", Icon = "?",
            Keybinding = "F1", Priority = 55, Execute = _ => a.ShowHelp(),
        });
        cmds.Add(new Command
        {
            Id = "global.quit", Label = "Quit", Category = "Global", Icon = "⏻",
            Keybinding = "Q", Priority = 50, Execute = _ => a.Quit(),
        });

        return cmds;
    }

    // "TLS / Certs" → "tls-certs"; "Raw Config" → "raw-config".
    private static string Slug(string label)
    {
        var chars = label.ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();
        return string.Join('-', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
