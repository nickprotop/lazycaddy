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
using LazyCaddy.UI.Modals;

namespace LazyCaddy.Views;

public sealed class RoutesView
{
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly EditCoordinator _editor;

    private TableControl? _table;

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
        if (_table.SelectedRow?.Tag is not Route route) return false;
        switch (key.Key)
        {
            case ConsoleKey.E:
                _ = EditUpstreamDialog.ShowAsync(_windowSystem, route, _editor);
                return true;
            case ConsoleKey.M:
                _ = EditMatcherDialog.ShowAsync(_windowSystem, route, _editor);
                return true;
            case ConsoleKey.N:
                _ = NewRouteWizard.ShowAsync(_windowSystem, _editor, ServerPathFor(route));
                return true;
            case ConsoleKey.D:
                _ = DeleteRouteAsync(route);
                return true;
            default:
                return false;
        }
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
            .AddLine($"[{muted}]Public host/match → internal upstream. Enter on a row for detail.[/]")
            .AddEmptyLine()
            .Build());

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
        _table.RowActivatedAsync += async (sender, rowIndex) =>
        {
            var route = _table?.SelectedRow?.Tag as Route;
            if (route is null) return;
            await RouteDetailModal.ShowAsync(_windowSystem, route);
        };

        panel.AddControl(_table);
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
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
