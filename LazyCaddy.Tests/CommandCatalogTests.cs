using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

/// <summary>
/// CommandCatalog builds the shell-owned commands (Navigation to each view + Global actions)
/// from injected delegates, so it's testable without a running shell. Asserts the catalog is
/// complete and its ids/categories/priorities are stable.
/// </summary>
public class CommandCatalogTests
{
    private static readonly IReadOnlyList<ViewDescriptor> Views = new[]
    {
        new ViewDescriptor(1, "Overview", "◈"),
        new ViewDescriptor(2, "Routes", "↦"),
        new ViewDescriptor(3, "TLS / Certs", "🔒"),
        new ViewDescriptor(9, "Server", "⚙"),
    };

    private static CommandCatalog.Actions NoopActions() => new(
        GoToView: _ => { },
        Refresh: () => { },
        QuickUndo: () => { },
        SnapshotNow: () => { },
        AdaptCaddyfile: () => { },
        ShowHelp: () => { },
        Quit: () => { });

    [Fact]
    public void Build_ProducesOneNavCommandPerView()
    {
        var cmds = CommandCatalog.Build(Views, NoopActions());

        var nav = cmds.Where(c => c.Category == "Go").ToList();
        Assert.Equal(Views.Count, nav.Count);
        Assert.Contains(nav, c => c.Id == "go.routes" && c.Label.Contains("Routes"));
        Assert.Contains(nav, c => c.Id == "go.server");
    }

    [Fact]
    public void Build_NavCommandJumpsToItsViewIndex()
    {
        int? jumped = null;
        var actions = NoopActions() with { GoToView = i => jumped = i };
        var cmds = CommandCatalog.Build(Views, actions);

        var routes = cmds.Single(c => c.Id == "go.routes");
        routes.Execute(new CommandContext(0, null, false, null));

        Assert.Equal(2, jumped); // Routes is view index 2
    }

    [Fact]
    public void Build_IncludesExpectedGlobalCommands()
    {
        var cmds = CommandCatalog.Build(Views, NoopActions());
        var ids = cmds.Select(c => c.Id).ToHashSet();

        Assert.Contains("global.refresh", ids);
        Assert.Contains("global.undo", ids);
        Assert.Contains("global.snapshot", ids);
        Assert.Contains("global.adapt", ids);
        Assert.Contains("global.help", ids);
        Assert.Contains("global.quit", ids);
    }

    [Fact]
    public void Build_GlobalCommandsInvokeTheirDelegates()
    {
        var hits = new List<string>();
        var actions = new CommandCatalog.Actions(
            GoToView: _ => { },
            Refresh: () => hits.Add("refresh"),
            QuickUndo: () => hits.Add("undo"),
            SnapshotNow: () => hits.Add("snapshot"),
            AdaptCaddyfile: () => hits.Add("adapt"),
            ShowHelp: () => hits.Add("help"),
            Quit: () => hits.Add("quit"));
        var cmds = CommandCatalog.Build(Views, actions);
        var ctx = new CommandContext(0, null, false, null);

        cmds.Single(c => c.Id == "global.refresh").Execute(ctx);
        cmds.Single(c => c.Id == "global.help").Execute(ctx);
        cmds.Single(c => c.Id == "global.quit").Execute(ctx);

        Assert.Equal(new[] { "refresh", "help", "quit" }, hits.ToArray());
    }

    [Fact]
    public void Build_Undo_DisabledWhenNoSnapshots()
    {
        var cmds = CommandCatalog.Build(Views, NoopActions());
        var undo = cmds.Single(c => c.Id == "global.undo");

        Assert.False(undo.IsEnabled(new CommandContext(0, null, HasSnapshots: false, null)));
        Assert.True(undo.IsEnabled(new CommandContext(0, null, HasSnapshots: true, null)));
        Assert.NotNull(undo.GetDisabledReason(new CommandContext(0, null, false, null)));
    }

    [Fact]
    public void Build_AllIdsUnique()
    {
        var cmds = CommandCatalog.Build(Views, NoopActions());
        var ids = cmds.Select(c => c.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
