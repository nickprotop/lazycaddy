// -----------------------------------------------------------------------
// LazyCaddy - Routes: one row per route (host/match -> upstream). Activating a
// row opens the route-detail modal with the pretty-printed config.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Models;
using LazyCaddy.Services;
using LazyCaddy.UI;
using LazyCaddy.UI.Modals;

namespace LazyCaddy.Views;

public sealed class RoutesView
{
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly EditCoordinator _editor;

    private TableControl? _table;
    private ToolbarControl? _toolbar;

    public RoutesView(ConsoleWindowSystem windowSystem, EditCoordinator editor)
    {
        _windowSystem = windowSystem;
        _editor = editor;
    }

    /// <summary>Handle a view-level edit shortcut. Returns true if consumed.
    /// Only acts when this view's table currently has focus.</summary>
    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (_table is null || !_table.HasFocus) return false;
        switch (key.Key)
        {
            case ConsoleKey.E:
                EditRoute();
                return true;
            case ConsoleKey.N:
                NewRoute();
                return true;
            case ConsoleKey.D:
                DeleteSelected();
                return true;
            default:
                return false;
        }
    }

    // ── Shared action handlers (invoked by both keys and toolbar buttons) ──
    // Each reads the currently-selected route at call time and no-ops if none.

    private Route? Selected => _table?.SelectedRow?.Tag as Route;

    private void EditRoute()
    {
        if (Selected is { } route) _ = EditRouteDialog.ShowAsync(_windowSystem, route, _editor);
    }

    private void NewRoute()
    {
        var server = Selected is { } route ? ServerPathFor(route) : "apps/http/servers/srv0";
        _ = NewRouteWizard.ShowAsync(_windowSystem, _editor, server);
    }

    private void DeleteSelected()
    {
        if (Selected is { } route) _ = DeleteRouteAsync(route);
    }

    // Derive the server path (apps/http/servers/<name>) from a route's ConfigPath.
    private static string ServerPathFor(Route route)
    {
        // route.ConfigPath looks like "apps/http/servers/srv0/routes/0"
        var idx = route.ConfigPath.IndexOf("/routes/", StringComparison.Ordinal);
        return idx > 0 ? route.ConfigPath[..idx] : "apps/http/servers/srv0";
    }

    private async Task DeleteRouteAsync(Route route)
    {
        if (string.IsNullOrEmpty(route.ConfigPath)) return;
        if (!await ConfirmDeleteDialog.ShowAsync(_windowSystem, $"route {route.HostOrMatch}")) return;
        await _editor.ApplyAsync(
            (admin, ct) => admin.DeleteConfigAsync(route.ConfigPath, ct),
            $"delete route {route.HostOrMatch}");
    }

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Routes[/]")
            .AddLine($"[{muted}]Public host → upstream. Select a row to edit.[/]")
            .AddEmptyLine()
            .Build());

        _toolbar = ViewToolbar.Create("routesToolbar");
        panel.AddControl(_toolbar);

        _table = Controls.Table()
            .AddColumn("Host / Match", TextJustification.Left)
            .AddColumn("Upstream", TextJustification.Left)
            .AddColumn("TLS", TextJustification.Center, 6)
            .AddColumn("Status", TextJustification.Left, 12)
            .Rounded()
            .WithBorderColor(UIConstants.MutedText)
            .Interactive()
            .WithSorting()
            .WithFiltering()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("routesTable")
            .Build();

        // Async activation handler: with InstallSynchronizationContext on, execution
        // resumes on the UI thread after the await, so touching controls is safe.
        // Resolve the route from the row's Tag (set in Update) rather than the raw
        // index, so it stays correct under the table's own sorting/filtering.
        // Enter / double-click on a route row opens the combined edit dialog.
        _table.RowActivatedAsync += async (sender, rowIndex) =>
        {
            if (_table?.SelectedRow?.Tag is Route r)
                await EditRouteDialog.ShowAsync(_windowSystem, r, _editor);
        };

        // Adaptive toolbar: rebuild when the selection changes so context buttons
        // (Delete) appear/disappear with a selected row.
        _table.SelectedRowChanged += (_, _) => RebuildToolbar();

        panel.AddControl(_table);
        RebuildToolbar();
    }

    private void RebuildToolbar()
    {
        if (_toolbar is null) return;
        var hasRow = Selected is not null;
        var actions = new List<ToolbarAction?>
        {
            new(ViewToolbar.Caption("✎", "Edit", "e"), EditRoute),
            new(ViewToolbar.Caption("⊕", "New", "n"), NewRoute),
        };
        if (hasRow)
        {
            actions.Add(null); // separator
            actions.Add(new(ViewToolbar.Caption("✕", "Delete", "d"), DeleteSelected));
        }
        ViewToolbar.Rebuild(_toolbar, actions);
    }

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null || _table is null) return;

        _table.ClearRows();
        foreach (var r in snap.Routes)
        {
            var tls = r.TlsEnabled
                ? $"[{UIConstants.Good.ToMarkup()}]✓[/]"
                : $"[{UIConstants.MutedText.ToMarkup()}]—[/]";
            _table.AddRow(new TableRow(
                Escape(r.HostOrMatch),
                Escape(r.Upstream),
                tls,
                UIConstants.StatusMarkup(r.Status))
            {
                Tag = r,
            });
        }
        RebuildToolbar();
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
