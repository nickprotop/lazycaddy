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
            case ConsoleKey.H:
                if (Selected is { TlsEnabled: true }) DisableHttps();
                else EnableHttps();
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
        if (Selected is { } route) _ = RouteEditModal.ShowAsync(_windowSystem, route, _editor);
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

    // Enable automatic HTTPS for the selected route's host by adding it to TLS
    // automation as a managed subject (Caddy then provisions + renews its cert).
    // TLS in Caddy is per-hostname (tls.automation), not a per-route flag.
    private void EnableHttps()
    {
        if (Selected is { } route && !route.TlsEnabled) _ = EnableHttpsAsync(route);
    }

    private async Task EnableHttpsAsync(Route route)
    {
        // Take just the hostnames from the matcher summary (drop any path part).
        var hosts = route.HostOrMatch
            .Split(' ')[0]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(h => h.Contains('.') || h.Contains(':') || !h.StartsWith('/'))
            .ToArray();
        if (hosts.Length == 0) return;

        var newJson = EditPatchBuilder.AcmePolicy(hosts);
        if (!await DiffConfirmDialog.ShowAsync(_windowSystem,
                $"Enable HTTPS for {string.Join(", ", hosts)}", "(new TLS policy)", newJson))
            return;

        await _editor.ApplyAsync(
            (admin, ct) => admin.PostConfigAsync("apps/tls/automation/policies", newJson, ct),
            $"enable HTTPS for {string.Join(", ", hosts)}");
    }

    // Stop managing TLS for the selected route's host: remove it from its automation
    // policy's subjects (or delete the policy if it was the only subject).
    private void DisableHttps()
    {
        if (Selected is { } route && route.TlsEnabled) _ = DisableHttpsAsync(route);
    }

    private async Task DisableHttpsAsync(Route route)
    {
        var host = route.HostOrMatch.Split(' ')[0]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(host)) return;

        string policiesJson;
        try { policiesJson = await _editor.GetConfigNodeAsync("apps/tls/automation/policies"); }
        catch { return; }

        using var doc = System.Text.Json.JsonDocument.Parse(policiesJson);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return;

        int polIdx = -1; string[] subjects = Array.Empty<string>();
        int i = 0;
        foreach (var pol in doc.RootElement.EnumerateArray())
        {
            if (pol.TryGetProperty("subjects", out var subs) && subs.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var list = subs.EnumerateArray().Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(e => e.GetString()!).ToArray();
                if (list.Contains(host)) { polIdx = i; subjects = list; break; }
            }
            i++;
        }
        if (polIdx < 0) return; // host not managed

        var remaining = subjects.Where(s => s != host).ToArray();
        string title, label;
        Func<ICaddyAdmin, CancellationToken, Task<WriteResult>> write;
        if (remaining.Length == 0)
        {
            // Sole subject → remove the whole policy.
            title = $"Disable HTTPS for {host} (remove TLS policy)";
            label = $"disable HTTPS for {host}";
            write = (admin, ct) => admin.DeleteConfigAsync($"apps/tls/automation/policies/{polIdx}", ct);
            if (!await DiffConfirmDialog.ShowAsync(_windowSystem, title,
                    System.Text.Json.JsonSerializer.Serialize(subjects), "(policy removed)")) return;
        }
        else
        {
            // Trim just this subject from the policy.
            title = $"Disable HTTPS for {host}";
            label = $"disable HTTPS for {host}";
            var newSubjects = System.Text.Json.JsonSerializer.Serialize(remaining);
            write = (admin, ct) => admin.PatchConfigAsync($"apps/tls/automation/policies/{polIdx}/subjects", newSubjects, ct);
            if (!await DiffConfirmDialog.ShowAsync(_windowSystem, title,
                    System.Text.Json.JsonSerializer.Serialize(subjects), newSubjects)) return;
        }
        await _editor.ApplyAsync(write, label);
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
                await RouteEditModal.ShowAsync(_windowSystem, r, _editor);
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
            // Toggle TLS for the host: Enable when off, Disable when on.
            if (Selected is { TlsEnabled: true })
                actions.Add(new(ViewToolbar.Caption("🔓", "Disable HTTPS", "h"), DisableHttps));
            else
                actions.Add(new(ViewToolbar.Caption("🔒", "Enable HTTPS", "h"), EnableHttps));
            actions.Add(new(ViewToolbar.Caption("✕", "Delete", "d"), DeleteSelected));
        }
        ViewToolbar.Rebuild(_toolbar, actions);
    }

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null || _table is null) return;

        // Preserve the selected row across the refresh so adaptive toolbar actions
        // (Edit/Delete/HTTPS) don't vanish on every poll tick; default to row 0.
        int prev = _table.SelectedRowIndex;
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
        RestoreSelection(prev, snap.Routes.Count);
        RebuildToolbar();
    }

    // Reselect a row after a data refresh: keep the prior index if still valid,
    // else select the first row so contextual actions stay available.
    private void RestoreSelection(int prevIndex, int count)
    {
        if (_table is null || count == 0) return;
        _table.SelectedRowIndex = prevIndex >= 0 && prevIndex < count ? prevIndex : 0;
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
