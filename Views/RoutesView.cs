// -----------------------------------------------------------------------
// LazyCaddy - Routes: a grouped, expandable table. Each route is a parent row
// (host/match → upstream); pressing Enter / → expands it to show its handler
// chain as indented child rows. Route-level actions (new/delete/HTTPS/edit
// match) act on a selected route row; handler-level actions (edit/add/reorder/
// delete) act on a selected handler child row. Mirrors the proven cxpost
// grouped-table pattern (PopulateThreadedList / RebuildThreadedTable).
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Models;
using LazyCaddy.Services;
using LazyCaddy.UI;
using LazyCaddy.UI.Modals;

namespace LazyCaddy.Views;

public sealed class RoutesView : ICommandProvider
{
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly Func<EditCoordinator> _editorFn;
    private EditCoordinator _editor => _editorFn();   // resolves the active EditCoordinator; keeps all _editor.X call sites identical

    private TableControl? _table;
    private ToolbarControl? _toolbar;
    private readonly HashSet<string> _expandedRoutes = new();   // keyed by route.ConfigPath
    private CaddySnapshot? _snapshot;                            // cached for toggle-rebuild
    private bool _isPopulating;                                  // reentrancy guard
    // Fingerprint of the rows currently displayed. Each poll builds a fresh (non-reference-equal)
    // snapshot, so without this guard the table would ClearRows()+rebuild every tick even when
    // nothing changed — flickering the table, re-firing SelectedRowChanged, and burning CPU. We
    // only rebuild when the visible content actually changes. (Focus survives a rebuild at the
    // framework level, so this is a perf/flicker optimization, not a focus fix.)
    private string? _renderedSignature;

    private static readonly HashSet<string> SecurityTypes = new() { "authentication", "headers", "rate_limit" };

    public RoutesView(ConsoleWindowSystem windowSystem, Func<EditCoordinator> editor)
    {
        _windowSystem = windowSystem;
        _editorFn = editor;
    }

    // ── Public surface DashboardShell depends on (signatures must not change) ──

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Routes[/]")
            .AddLine($"[{muted}]Public host → upstream. Enter/→ expands a route's handler chain.[/]")
            .AddEmptyLine()
            .Build());

        _toolbar = ViewToolbar.Create("routesToolbar");
        panel.AddControl(_toolbar);

        // Grouped rows don't compose with the table's built-in sort/filter, so those
        // are intentionally dropped — routes stay in config (handler-execution) order.
        _table = Controls.Table()
            .AddColumn("Host / Match", TextJustification.Left)
            .AddColumn("Upstream", TextJustification.Left)
            .AddColumn("Listen", TextJustification.Left, 16)
            .AddColumn("TLS", TextJustification.Center, 6)
            .AddColumn("Status", TextJustification.Left, 12)
            .Rounded()
            .WithBorderColor(UIConstants.MutedText)
            .Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("routesTable")
            .Build();

        // Adaptive toolbar follows the selected level (route vs handler).
        _table.SelectedRowChanged += (_, _) => RebuildToolbar();
        // Enter / double-click: route row → toggle expand; handler row → edit.
        _table.RowActivatedAsync += async (_, _) => await OnActivateAsync();

        panel.AddControl(_table);
        RebuildToolbar();

        // NavigationView rebuilds this view (a fresh empty table) each time it's reopened. Reset
        // the render fingerprint so the next Update repopulates rather than skipping as unchanged.
        _renderedSignature = null;
    }

    public void Update(DashboardState state) => Populate(state.Snapshot);

    /// <summary>Focus the routes table so its keys work immediately on view entry.</summary>
    public void FocusPrimary() => _table?.RequestFocus();

    /// <summary>Handle a view-level edit shortcut. Returns true if consumed.
    /// Only acts when this view's table currently has focus.</summary>
    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (_table is null) return false;
        DebugLog.Line($"RoutesView.TryHandleKey key={key.Key} tableHasFocus={_table.HasFocus} " +
            $"selRow={_table.SelectedRowIndex} selTag={_table.SelectedRow?.Tag?.GetType().Name ?? "null"}");
        if (!_table.HasFocus) return false;
        var tag = _table.SelectedRow?.Tag;

        if (tag is Route route)
        {
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                case ConsoleKey.RightArrow:
                    ToggleExpand(route);
                    return true;
                case ConsoleKey.E:
                    EditMatch(route);
                    return true;
                case ConsoleKey.N:
                    NewRoute();
                    return true;
                case ConsoleKey.D:
                    _ = DeleteRouteAsync(route);
                    return true;
                case ConsoleKey.A:
                    _ = AddHandlerAsync(route);
                    return true;
                case ConsoleKey.I:
                    _ = IpAccessAsync(route);
                    return true;
                case ConsoleKey.H:
                    if (route.TlsEnabled) _ = DisableHttpsAsync(route);
                    else _ = EnableHttpsAsync(route);
                    return true;
                default:
                    return false;
            }
        }

        if (tag is HandlerDescriptor hd)
        {
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    _ = EditHandlerAsync(hd);
                    return true;
                case ConsoleKey.D:
                    _ = DeleteHandlerAsync(hd);
                    return true;
                case ConsoleKey.A:
                    if (ParentRouteOf(hd) is { } parent) _ = AddHandlerAsync(parent);
                    return true;
                case ConsoleKey.Add:
                case ConsoleKey.OemPlus:
                    _ = ReorderAsync(hd, +1);
                    return true;
                case ConsoleKey.Subtract:
                case ConsoleKey.OemMinus:
                    _ = ReorderAsync(hd, -1);
                    return true;
                case ConsoleKey.LeftArrow:
                    if (ParentRouteOf(hd) is { } p) Collapse(p);
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    // ── Activation (Enter / double-click) ──

    private async Task OnActivateAsync()
    {
        var tag = _table?.SelectedRow?.Tag;
        DebugLog.Line($"RoutesView.OnActivate selTag={tag?.GetType().Name ?? "null"}");
        if (tag is Route route) ToggleExpand(route);
        else if (tag is HandlerDescriptor hd) await EditHandlerAsync(hd);
    }

    // ── Expand / collapse ──

    private void ToggleExpand(Route route)
    {
        if (!_expandedRoutes.Remove(route.ConfigPath))
            _expandedRoutes.Add(route.ConfigPath);
        DebugLog.Line($"RoutesView.ToggleExpand {route.ConfigPath} → expanded={_expandedRoutes.Contains(route.ConfigPath)}");
        RebuildRows();
        SelectRouteRow(route.ConfigPath);
    }

    private void Collapse(Route route)
    {
        if (_expandedRoutes.Remove(route.ConfigPath))
        {
            RebuildRows();
            SelectRouteRow(route.ConfigPath);
        }
    }

    // ── Populate / rebuild (cxpost grouped-table pattern) ──

    // PopulateThreadedList analog: fan a fresh snapshot into grouped rows, preserving
    // selection identity (route/handler ConfigPath) and scroll across the refresh.
    private void Populate(CaddySnapshot? snap)
    {
        if (snap is null || _table is null || _isPopulating) return;
        _snapshot = snap;
        // Prune expanded IDs for routes that no longer exist.
        _expandedRoutes.IntersectWith(snap.Routes.Select(r => r.ConfigPath));

        // Skip the teardown/rebuild entirely when the visible content is unchanged — the common
        // case, since every poll hands us a fresh-but-identical snapshot. Avoids per-tick
        // ClearRows() churn (flicker, spurious SelectedRowChanged, CPU).
        var sig = ComputeSignature(snap.Routes);
        if (sig == _renderedSignature) { RebuildToolbar(); return; }

        _isPopulating = true;
        try { BuildRowsCore(snap.Routes); }
        finally { _isPopulating = false; }
        _renderedSignature = sig;
        RebuildToolbar();
    }

    // RebuildThreadedTable analog: re-lay the rows from the cached snapshot (no new
    // data) after an expand/collapse toggle. Always rebuilds (the expansion set changed),
    // then refreshes the signature so the next poll doesn't redundantly rebuild.
    private void RebuildRows()
    {
        if (_snapshot is null || _table is null || _isPopulating) return;
        _isPopulating = true;
        try { BuildRowsCore(_snapshot.Routes); }
        finally { _isPopulating = false; }
        _renderedSignature = ComputeSignature(_snapshot.Routes);
        RebuildToolbar();
    }

    // A fingerprint of exactly what determines the rendered rows: each route's identity and the
    // visible columns, plus whether it's expanded (which adds its handler child rows).
    private string ComputeSignature(IReadOnlyList<Route> routes)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in routes)
        {
            bool expanded = _expandedRoutes.Contains(r.ConfigPath);
            sb.Append(r.ConfigPath).Append('|')
              .Append(r.HostOrMatch).Append('|')
              .Append(r.Upstream).Append('|')
              .Append(r.Listen).Append('|')
              .Append(r.TlsEnabled ? '1' : '0').Append('|')
              .Append(r.Status).Append('|')
              .Append(expanded ? 'E' : 'c');
            // Expanded routes render handler child rows from RawConfigJson, so a handler edit that
            // leaves host/upstream/status unchanged must still force a rebuild — fold the config in.
            if (expanded) sb.Append('|').Append(r.RawConfigJson);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // Shared core: capture selection + scroll, clear, re-add route rows (with handler
    // child rows for expanded routes), then restore selection identity + scroll.
    private void BuildRowsCore(IReadOnlyList<Route> routes)
    {
        if (_table is null) return;

        // Capture selection identity by Tag (ConfigPath) and the scroll position.
        string? selPath = _table.SelectedRow?.Tag switch
        {
            Route r => r.ConfigPath,
            HandlerDescriptor hd => hd.ConfigPath,
            _ => null,
        };
        var savedScroll = _table.ScrollOffset;

        _table.ClearRows();
        foreach (var route in routes)
        {
            _table.AddRow(BuildRouteRow(route));
            if (_expandedRoutes.Contains(route.ConfigPath))
            {
                foreach (var hd in HandlersOf(route))
                    _table.AddRow(BuildHandlerRow(hd));
            }
        }

        // Restore selection by matching Tag identity.
        if (selPath is not null)
        {
            for (int i = 0; i < _table.RowCount; i++)
            {
                var t = _table.GetRow(i).Tag;
                var p = t switch { Route r => r.ConfigPath, HandlerDescriptor hd => hd.ConfigPath, _ => null };
                if (p == selPath) { _table.SelectedRowIndex = i; break; }
            }
        }
        if (_table.RowCount > 0)
        {
            if (_table.SelectedRowIndex < 0 || _table.SelectedRowIndex >= _table.RowCount)
                _table.SelectedRowIndex = 0;
            _table.ScrollOffset = savedScroll;
        }
    }

    // The non-subroute handlers of a route (subroute containers are skipped as rows;
    // their children follow with composed ConfigPaths).
    private IReadOnlyList<HandlerDescriptor> HandlersOf(Route route)
    {
        try
        {
            return RouteModel.ParseHandlers(route.RawConfigJson, route.ConfigPath)
                .Where(h => h.Type != "subroute").ToList();
        }
        catch { return Array.Empty<HandlerDescriptor>(); }
    }

    // ── Row builders ──

    private TableRow BuildRouteRow(Route r)
    {
        var expanded = _expandedRoutes.Contains(r.ConfigPath);
        var accent = UIConstants.Accent.ToMarkup();
        string arrow;
        if (expanded)
            arrow = $"[{accent}]▾[/]";
        else
        {
            var count = HandlersOf(r).Count;
            arrow = count > 0 ? $"[{accent}]▸{count}[/]" : $"[{accent}]▸[/]";
        }

        var tls = r.TlsEnabled
            ? $"[{UIConstants.Good.ToMarkup()}]✓[/]"
            : $"[{UIConstants.MutedText.ToMarkup()}]—[/]";

        return new TableRow(
            $"{arrow} {Escape(r.HostOrMatch)}",
            Escape(r.Upstream),
            Escape(r.Listen),
            tls,
            UIConstants.StatusMarkup(r.Status))
        {
            Tag = r,
        };
    }

    private TableRow BuildHandlerRow(HandlerDescriptor hd)
    {
        var muted = UIConstants.MutedText.ToMarkup();
        return new TableRow(
            $"    [{muted}]{Escape(hd.Type)}[/]",
            Escape(hd.Summary),
            "",
            "",
            "")
        {
            Tag = hd,
        };
    }

    // ── Toolbar (adaptive to selected level) ──

    private void RebuildToolbar()
    {
        if (_toolbar is null) return;
        var tag = _table?.SelectedRow?.Tag;

        List<ToolbarAction?> actions;
        if (tag is HandlerDescriptor hd)
        {
            actions = new List<ToolbarAction?>
            {
                new(ViewToolbar.Caption("✎", "Edit", "↵"), () => _ = EditHandlerAsync(hd)),
                new(ViewToolbar.Caption("⊕", "Add", "a"), () => { if (ParentRouteOf(hd) is { } p) _ = AddHandlerAsync(p); }),
                null,
                new(ViewToolbar.Caption("▲", "Up", "-"), () => _ = ReorderAsync(hd, -1)),
                new(ViewToolbar.Caption("▼", "Down", "+"), () => _ = ReorderAsync(hd, +1)),
                null,
                new(ViewToolbar.Caption("✕", "Delete", "d"), () => _ = DeleteHandlerAsync(hd)),
            };
        }
        else
        {
            var route = tag as Route;
            actions = new List<ToolbarAction?>
            {
                new(ViewToolbar.Caption("⊕", "New", "n"), NewRoute),
            };
            if (route is not null)
            {
                actions.Add(new(ViewToolbar.Caption("✎", "Edit match", "e"), () => EditMatch(route)));
                actions.Add(new(ViewToolbar.Caption("⚙", "Add handler", "a"), () => _ = AddHandlerAsync(route)));
                actions.Add(new(ViewToolbar.Caption("⛒", "IP access", "i"), () => _ = IpAccessAsync(route)));
                if (route.TlsEnabled)
                    actions.Add(new(ViewToolbar.Caption("🔓", "Disable HTTPS", "h"), () => _ = DisableHttpsAsync(route)));
                else
                    actions.Add(new(ViewToolbar.Caption("🔒", "Enable HTTPS", "h"), () => _ = EnableHttpsAsync(route)));
                actions.Add(null);
                actions.Add(new(ViewToolbar.Caption("✕", "Delete", "d"), () => _ = DeleteRouteAsync(route)));
            }
        }
        ViewToolbar.Rebuild(_toolbar, actions);
    }

    // ── Selection helpers ──

    private void SelectRouteRow(string configPath)
    {
        if (_table is null) return;
        for (int i = 0; i < _table.RowCount; i++)
        {
            if (_table.GetRow(i).Tag is Route r && r.ConfigPath == configPath)
            {
                _table.SelectedRowIndex = i;
                return;
            }
        }
    }

    // Find the route whose ConfigPath prefixes this handler's path.
    private Route? ParentRouteOf(HandlerDescriptor hd)
    {
        if (_snapshot is null) return null;
        return _snapshot.Routes
            .Where(r => !string.IsNullOrEmpty(r.ConfigPath) && hd.ConfigPath.StartsWith(r.ConfigPath + "/", StringComparison.Ordinal))
            .OrderByDescending(r => r.ConfigPath.Length)   // most-specific prefix wins
            .FirstOrDefault();
    }

    // ── Handler-level actions ──

    private async Task EditHandlerAsync(HandlerDescriptor hd)
    {
        if (ParentRouteOf(hd) is { } route)
            await SingleHandlerEditModal.ShowAsync(_windowSystem, route, hd, _editor);
    }

    private async Task DeleteHandlerAsync(HandlerDescriptor hd)
    {
        if (string.IsNullOrEmpty(hd.ConfigPath)) return;
        if (!await ConfirmDeleteDialog.ShowAsync(_windowSystem, $"handler {hd.Type}")) return;
        await _editor.ApplyAsync(
            RouteOp.Delete(hd.ConfigPath, "{}", $"delete {hd.Type} handler"),
            $"delete {hd.Type} handler");
    }

    // Reorder a handler within its handle[] array. The array path is the handler's
    // ConfigPath minus the trailing /{N}; the index is that N.
    private async Task ReorderAsync(HandlerDescriptor hd, int delta)
    {
        var slash = hd.ConfigPath.LastIndexOf('/');
        if (slash <= 0) return;
        var arrayPath = hd.ConfigPath[..slash];
        if (!int.TryParse(hd.ConfigPath[(slash + 1)..], out var index)) return;

        string arrayJson;
        try { arrayJson = await _editor.GetConfigNodeAsync(arrayPath); }
        catch { return; }

        var newJson = HandlerReorder.Swap(arrayJson, index, delta);
        if (newJson == arrayJson) return; // out of bounds / no-op

        await _editor.ApplyAsync(
            RouteOp.Field(arrayPath, newJson, "reorder handlers"),
            "reorder handlers");
    }

    // Append (or splice) a minimal handler of a chosen type into the route's primary handle[] array.
    // Security handler types (authentication, headers, rate_limit) are inserted BEFORE the
    // first terminal handler so they actually apply; all other types are appended.
    private async Task AddHandlerAsync(Route route)
    {
        var type = await HandlerTypePicker.ShowAsync(_windowSystem);
        if (string.IsNullOrEmpty(type)) return;

        if (type == "forward_auth")
        {
            var choice = await ForwardAuthModal.ShowAsync(_windowSystem);
            if (choice is not { } fa) return;
            var fjson = SecurityHandlerPatch.ForwardAuth(fa.Provider, fa.Upstream);
            var arrF = ResolvePrimaryHandlerArray(route, route.RawConfigJson);
            string arrJsonF;
            try { arrJsonF = await _editor.GetConfigNodeAsync(arrF); } catch { arrJsonF = "[]"; }
            var idxF = SecurityHandlerPlacement.InsertIndex(arrJsonF);
            var splicedF = SpliceAt(arrJsonF, idxF, fjson);
            var resF = await _editor.ApplyAsync(RouteOp.Field(arrF, splicedF, "add forward_auth handler"), "add forward_auth handler");
            if (resF.Success) _expandedRoutes.Add(route.ConfigPath);
            return;
        }

        var arr = ResolvePrimaryHandlerArray(route, route.RawConfigJson);
        var json = NewRouteSkeleton.MinimalHandler(type);

        WriteResult res;
        if (SecurityTypes.Contains(type))
        {
            string arrayJson;
            try { arrayJson = await _editor.GetConfigNodeAsync(arr); }
            catch { arrayJson = "[]"; }
            var idx = SecurityHandlerPlacement.InsertIndex(arrayJson);
            var spliced = SpliceAt(arrayJson, idx, json);
            res = await _editor.ApplyAsync(RouteOp.Field(arr, spliced, $"add {type} handler"), $"add {type} handler");
        }
        else
        {
            res = await _editor.ApplyAsync(RouteOp.Add(arr, json, $"add {type} handler"), $"add {type} handler");
        }

        // Expand the route so the new handler shows on the next poll.
        if (res.Success) _expandedRoutes.Add(route.ConfigPath);
    }

    // Insert `elementJson` into the JSON array `arrayJson` at `index` (clamped). Returns new array JSON.
    private static string SpliceAt(string arrayJson, int index, string elementJson)
    {
        try
        {
            var arr = System.Text.Json.Nodes.JsonNode.Parse(arrayJson) as System.Text.Json.Nodes.JsonArray ?? new();
            var el = System.Text.Json.Nodes.JsonNode.Parse(elementJson);
            index = Math.Clamp(index, 0, arr.Count);
            arr.Insert(index, el);
            return arr.ToJsonString();
        }
        catch { return arrayJson; }
    }

    // The config path of the route's primary handle[] array — derived from the first
    // non-subroute handler's path, else the conventional {route}/handle.
    private static string ResolvePrimaryHandlerArray(Route route, string routeJson)
    {
        try
        {
            var first = RouteModel.ParseHandlers(routeJson, route.ConfigPath).FirstOrDefault(d => d.Type != "subroute");
            if (first is not null)
            {
                var i = first.ConfigPath.LastIndexOf('/');
                if (i > 0) return first.ConfigPath[..i];
            }
        }
        catch { }
        return $"{route.ConfigPath}/handle";
    }

    // ── Route-level actions (preserved from the flat view) ──

    /// <summary>The currently-selected route row tag (for the command portal's context).</summary>
    public object? SelectedTag => _table?.SelectedRow?.Tag;

    private const int RoutesViewIndex = 2;

    public IEnumerable<Command> GetCommands()
    {
        // Enabled only while viewing Routes (so the route actions target the visible selection).
        bool OnRoutes(CommandContext c) => c.CurrentViewIndex == RoutesViewIndex;
        bool OnRouteRow(CommandContext c) => OnRoutes(c) && c.SelectedTag is Route;
        string RowReason(CommandContext c) => OnRoutes(c) ? "select a route first" : "go to Routes";

        yield return new Command
        {
            Id = "routes.new", Label = "New route", Category = "Routes", Icon = "⊕",
            Keybinding = "n", Priority = 70,
            CanExecute = OnRoutes, DisabledReason = _ => "go to Routes",
            Execute = _ => NewRoute(),
        };
        yield return new Command
        {
            Id = "routes.edit-match", Label = "Edit route match", Category = "Routes", Icon = "✎",
            Keybinding = "e", Priority = 69,
            CanExecute = OnRouteRow, DisabledReason = RowReason,
            Execute = c => { if (c.SelectedTag is Route r) EditMatch(r); },
        };
        yield return new Command
        {
            Id = "routes.ip-access", Label = "IP access (allow/deny)", Category = "Routes", Icon = "⛒",
            Keybinding = "i", Priority = 66,
            CanExecute = OnRouteRow, DisabledReason = RowReason,
            Execute = c => { if (c.SelectedTag is Route r) _ = IpAccessAsync(r); },
        };
        yield return new Command
        {
            Id = "routes.toggle-https", Label = "Enable/Disable HTTPS", Category = "Routes", Icon = "🔒",
            Keybinding = "h", Priority = 65,
            CanExecute = OnRouteRow, DisabledReason = RowReason,
            Execute = c => { if (c.SelectedTag is Route r) { if (r.TlsEnabled) _ = DisableHttpsAsync(r); else _ = EnableHttpsAsync(r); } },
        };
        yield return new Command
        {
            Id = "routes.delete", Label = "Delete route", Category = "Routes", Icon = "✕",
            Keybinding = "d", Priority = 60,
            CanExecute = OnRouteRow, DisabledReason = RowReason,
            Execute = c => { if (c.SelectedTag is Route r) _ = DeleteRouteAsync(r); },
        };
    }

    private void EditMatch(Route route)
    {
        _ = MatchEditModal.ShowAsync(_windowSystem, route, _editor);
    }

    private async Task IpAccessAsync(Route route)
    {
        await IpAccessModal.ShowAsync(_windowSystem, route, _editor);
    }

    private void NewRoute()
    {
        // Seed the wizard with the selected route's server. With nothing selected, use the first
        // server that actually exists rather than assuming one named "srv0" is there -- on a
        // multi-server config that guess silently wrote the route to the wrong server.
        var selected = _table?.SelectedRow?.Tag as Route;
        var server = selected is { } route
            ? ServerPathFor(route)
            : _snapshot?.Routes.Select(r => r.ServerName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) is { } first
                ? $"apps/http/servers/{first}"
                : "";
        _ = NewRouteWizard.ShowAsync(_windowSystem, _editor, server);
    }

    // Enable automatic HTTPS for the route's host by adding it to TLS automation as a
    // managed subject (Caddy then provisions + renews its cert). TLS in Caddy is
    // per-hostname (tls.automation), not a per-route flag.
    private async Task EnableHttpsAsync(Route route)
    {
        if (route.TlsEnabled) return;
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
            RouteOp.Add("apps/tls/automation/policies", newJson, $"enable HTTPS for {string.Join(", ", hosts)}"),
            $"enable HTTPS for {string.Join(", ", hosts)}");
    }

    // Stop managing TLS for the route's host: remove it from its automation policy's
    // subjects (or delete the policy if it was the only subject).
    private async Task DisableHttpsAsync(Route route)
    {
        if (!route.TlsEnabled) return;
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
        RouteOp op;
        if (remaining.Length == 0)
        {
            // Sole subject → remove the whole policy.
            title = $"Disable HTTPS for {host} (remove TLS policy)";
            label = $"disable HTTPS for {host}";
            op = RouteOp.Delete($"apps/tls/automation/policies/{polIdx}", "{}", label);
            if (!await DiffConfirmDialog.ShowAsync(_windowSystem, title,
                    System.Text.Json.JsonSerializer.Serialize(subjects), "(policy removed)")) return;
        }
        else
        {
            // Trim just this subject from the policy.
            title = $"Disable HTTPS for {host}";
            label = $"disable HTTPS for {host}";
            var newSubjects = System.Text.Json.JsonSerializer.Serialize(remaining);
            op = RouteOp.Field($"apps/tls/automation/policies/{polIdx}/subjects", newSubjects, label);
            if (!await DiffConfirmDialog.ShowAsync(_windowSystem, title,
                    System.Text.Json.JsonSerializer.Serialize(subjects), newSubjects)) return;
        }
        await _editor.ApplyAsync(op, label);
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
            RouteOp.Delete(route.ConfigPath, "{}", $"delete route {route.HostOrMatch}"),
            $"delete route {route.HostOrMatch}");
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
