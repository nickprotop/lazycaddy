// -----------------------------------------------------------------------
// LazyCaddy - the consolidated route edit modal: one modal, a tab strip of
// IConfigEditors, ONE combined diff, and a single batched apply. It also hosts
// staged structural edits — add a handler (Ctrl+N) / delete a handler (Ctrl+D) —
// which are collected alongside the per-field writes and applied as ONE mixed
// op batch via EditCoordinator.ApplyOpsAsync.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;
using LazyCaddy.UI.Editors;

namespace LazyCaddy.UI.Modals;

public sealed class RouteEditModal : ModalBase<bool>
{
    private readonly Route _route;
    private readonly EditCoordinator _editor;

    private readonly List<IConfigEditor> _editors = new();
    private TabControl? _tabControl;
    private MarkupControl? _status;

    // The route's primary handler-chain array path (e.g. ".../handle/0/routes/0/handle"
    // for a subroute-wrapped route, or ".../handle" for a flat route). New handlers
    // (Ctrl+N) always append here. Computed in BuildContent.
    private string _primaryHandlerArray = "";

    // The route node JSON the editors are built from. Init'd from the route's captured
    // JSON; re-fetched live on RebuildTabs after a successful apply so handler indices
    // / sub-tabs reflect the new shape.
    private string _routeJson;

    // --- Staged structural edits (not yet applied) ---------------------------------
    // Handlers added this session: each is a real editor tab over an in-memory minimal
    // node, paired with the array to POST to and the chosen type.
    private readonly List<(IConfigEditor editor, string arrayPath, string type)> _stagedAdds = new();
    // Delete ops for EXISTING handlers the user removed (staged until apply).
    private readonly List<RouteOp> _stagedDeletes = new();
    // Editors that are staged-adds (so ApplyAll emits an Add op for them, not a Field op,
    // and Ctrl+D just drops them rather than staging a Delete).
    private readonly HashSet<IConfigEditor> _addEditors = new();

    private RouteEditModal(Route route, EditCoordinator editor)
    {
        _route = route; _editor = editor; _routeJson = route.RawConfigJson;
    }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new RouteEditModal(route, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Edit route — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (96, 32);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();

        _primaryHandlerArray = ResolvePrimaryHandlerArray();
        _editors.AddRange(BuildEditors());

        if (_editors.Count == 0)
        {
            Modal.AddControl(Controls.Markup()
                .AddLine($"[{muted}]No editable handlers on this route (subroute/unknown handlers are not editable here).[/]")
                .WithMargin(2, 1, 2, 0).Build());
        }
        else
        {
            var tabs = Controls.TabControl().Fill().WithName("routeEditTabs");
            foreach (var ed in _editors)
            {
                var panel = Controls.ScrollablePanel().Build();
                if (ed is ReverseProxyEditor rp) rp.OnError = ShowError;
                ed.Build(panel, MarkDirty);
                tabs.AddTab(ed.TabTitle, panel);
            }
            tabs.WithActiveTab(0);
            _tabControl = tabs.Build();
            Modal.AddControl(_tabControl);
        }

        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Ctrl+S: apply all   Ctrl+N: add handler   Ctrl+D: delete handler   Esc: close[/]")
            .WithMargin(2, 0, 2, 0).StickyBottom().Build());
        _status = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_status);

        RunGuarded(LoadAllAsync, ShowError);
    }

    // The route's primary handler-chain array path: the FIRST real (non-subroute) handler's
    // ConfigPath minus its trailing "/{index}" segment. For a subroute-wrapped route the first
    // real handler sits at ".../handle/0/routes/0/handle/0" → array ".../handle/0/routes/0/handle".
    // For a flat route it's ".../handle". Falls back to "{route}/handle" if parsing finds nothing.
    private string ResolvePrimaryHandlerArray()
    {
        try
        {
            var descriptors = RouteModel.ParseHandlers(_routeJson, _route.ConfigPath);
            var first = descriptors.FirstOrDefault(d => d.Type != "subroute");
            if (first is not null)
            {
                var i = first.ConfigPath.LastIndexOf('/');
                if (i > 0) return first.ConfigPath[..i];
            }
        }
        catch { /* fall through */ }
        return $"{_route.ConfigPath}/handle";
    }

    // Assemble the ordered editor (tab) list for the route. (See prior revisions for the
    // full rationale.) Match first, then every real handler's editor tab(s) from _routeJson.
    private List<IConfigEditor> BuildEditors()
    {
        var list = new List<IConfigEditor>();
        list.Add(new MatchEditor(_route.ConfigPath));

        IReadOnlyList<HandlerDescriptor> descriptors;
        try { descriptors = RouteModel.ParseHandlers(_routeJson, _route.ConfigPath); }
        catch { return list; }

        foreach (var d in descriptors)
        {
            if (d.Type == "subroute") continue; // container, not an editable leaf; its children follow
            list.AddRange(EditorsForType(d.Type, d.ConfigPath));
        }
        return list;
    }

    // The peer editor tabs for a handler of `type` at node path `p`. reverse_proxy and
    // file_server expand into several peer tabs; everything else is one editor.
    private static IEnumerable<IConfigEditor> EditorsForType(string type, string p)
    {
        switch (type)
        {
            case "reverse_proxy":
                yield return new ReverseProxyEditor(p);              // "Upstreams"
                yield return new LoadBalancingEditor(p);             // "Load balancing"
                yield return new HealthChecksEditor(p);              // "Health checks"
                yield return new HttpTransportEditor(p);             // "Transport"
                yield return new TlsConfigEditor($"{p}/transport/tls");
                yield return new KeepAliveEditor($"{p}/transport");
                yield return new HeadersEditor($"{p}/headers");
                break;
            case "file_server":
                yield return new FileServerEditor(p);                // "File server"
                yield return new BrowseEditor(p);                    // "Browse"
                break;
            case "static_response":
                yield return new StaticResponseEditor(p);
                break;
            case "error":
                yield return new ErrorEditor(p);
                break;
            case "rewrite":
                yield return new RewriteEditor(p);
                break;
            case "headers":
                yield return new HeadersEditor(p);
                break;
            case "encode":
                yield return new EncodeEditor(p);
                break;
            case "vars":
                yield return new VarsEditor(p);
                break;
            case "request_body":
                yield return new RequestBodyEditor(p);
                break;
            case "templates":
                yield return new TemplatesEditor(p);
                break;
            case "authentication":
                yield return new AuthenticationEditor(p);
                break;
            default:
                break;
        }
    }

    // The single PRIMARY editor for a staged-add of `type`, at the placeholder node path `p`.
    private async Task LoadAllAsync()
    {
        foreach (var e in _editors)
        {
            if (_addEditors.Contains(e)) continue; // staged-adds have no node to GET
            await e.LoadAsync(_editor);
        }
        MarkDirty(); // refresh tab markers against the freshly-loaded (clean) baseline
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0;

        // Ctrl+S → apply all (staged ops + per-field writes) in one batch.
        if (e.KeyInfo.Key == ConsoleKey.S && ctrl)
        {
            e.Handled = true;
            RunGuarded(ApplyAllAsync, ShowError);
            return;
        }
        // Ctrl+N → add a handler (Ctrl-gated so it never collides with typing in a field).
        if (e.KeyInfo.Key == ConsoleKey.N && ctrl)
        {
            e.Handled = true;
            RunGuarded(async () =>
            {
                var type = await HandlerTypePicker.ShowAsync(WindowSystem, Modal);
                if (type is null) return;
                AddHandlerTab(type);
            }, ShowError);
            return;
        }
        // Ctrl+D → delete the active handler (or drop a staged-add).
        if (e.KeyInfo.Key == ConsoleKey.D && ctrl)
        {
            e.Handled = true;
            var ae = ActiveEditor;
            if (ae is null) return;
            if (ae is MatchEditor) { ShowError("The Match tab can't be deleted."); return; }
            RunGuarded(async () =>
            {
                if (_addEditors.Contains(ae)) { RemoveStagedAdd(ae); return; }
                if (!await ConfirmDeleteDialog.ShowAsync(WindowSystem, $"handler {HandlerLabelOf(ae)}", Modal)) return;
                await StageDeleteHandlerAsync(ae);
            }, ShowError);
            return;
        }
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            e.Handled = true;
            if (AnyPending())
            {
                RunGuarded(async () =>
                {
                    var choice = await UnsavedChangesDialog.ShowAsync(WindowSystem, Modal);
                    if (choice == UnsavedChoice.Apply)
                    {
                        await ApplyAllAsync();
                        if (!AnyPending()) CloseWithResult(true);
                    }
                    else if (choice == UnsavedChoice.Discard)
                    {
                        CloseWithResult(false);
                    }
                    // Cancel → stay in the modal.
                }, ShowError);
            }
            else
            {
                CloseWithResult(false);
            }
            return;
        }
        // Otherwise route to the active tab's editor.
        if (ActiveEditor?.HandleKey(e.KeyInfo) == true)
            e.Handled = true;
    }

    private bool AnyPending() => _editors.Any(ed => ed.IsDirty) || _stagedAdds.Count > 0 || _stagedDeletes.Count > 0;

    private IConfigEditor? ActiveEditor
    {
        get
        {
            if (_tabControl is null || _editors.Count == 0) return null;
            var i = _tabControl.ActiveTabIndex;
            return i >= 0 && i < _editors.Count ? _editors[i] : null;
        }
    }

    // === Ctrl+N: stage a new handler ===============================================
    // Build the type's primary editor over a placeholder node path (never GET/LoadAsync'd),
    // add it as a tab, and record it as a staged-add appended to the route's primary handler
    // chain. On Apply it becomes a POST of MinimalHandler(type) merged with the editor's
    // current field values.
    private void AddHandlerTab(string type)
    {
        if (_tabControl is null)
        {
            // The route had no editable handlers, so no TabControl exists yet — nothing to add into.
            ShowError("Cannot add a handler to this route.");
            return;
        }

        // A staged-add gets a READ-ONLY placeholder tab (no editable editor): the handler node
        // doesn't exist yet, and the per-handler editors write sub-node FRAGMENTS, not a whole
        // node. On Apply we POST NewRouteSkeleton.MinimalHandler(type) — one valid handler — then
        // re-parse, after which the real editor + sub-tabs appear for configuration.
        var ed = new StagedAddPlaceholderEditor(type);
        var panel = Controls.ScrollablePanel().Build();
        ed.Build(panel, MarkDirty);
        _tabControl.AddTab(ed.TabTitle, panel);
        _editors.Add(ed);
        _addEditors.Add(ed);
        _stagedAdds.Add((ed, _primaryHandlerArray, type));

        _tabControl.ActiveTabIndex = _editors.Count - 1; // focus the new tab
        MarkDirty();
        SetStatus($"[{UIConstants.MutedText.ToMarkup()}]Staged new {Escape(type)} handler (Ctrl+S to apply, then configure).[/]");
    }

    // Drop a staged-add that was never applied: remove its tab + bookkeeping.
    private void RemoveStagedAdd(IConfigEditor ed)
    {
        var idx = _editors.IndexOf(ed);
        if (idx < 0) return;
        _editors.RemoveAt(idx);
        _addEditors.Remove(ed);
        _stagedAdds.RemoveAll(s => ReferenceEquals(s.editor, ed));
        _tabControl?.RemoveTab(idx);
        MarkDirty();
        SetStatus($"[{UIConstants.MutedText.ToMarkup()}]Removed staged handler.[/]");
    }

    // === Ctrl+D: stage delete of an existing handler ==============================
    // Deleting "the handler" means deleting the whole handler NODE and removing ALL its
    // tabs (a reverse_proxy spans Upstreams/LB/Health/Transport/TLS/Keep-alive/Headers).
    private async Task StageDeleteHandlerAsync(IConfigEditor ed)
    {
        var nodePath = HandlerNodePath(ed.ConfigPath);
        if (nodePath is null) { ShowError("Could not resolve the handler to delete."); return; }

        // Dedupe (same handler may be reachable from multiple of its sub-tabs).
        if (!_stagedDeletes.Any(o => o.Path == nodePath))
        {
            string oldJson;
            try { oldJson = await _editor.GetConfigNodeAsync(nodePath); }
            catch { oldJson = "{}"; }
            if (string.IsNullOrWhiteSpace(oldJson)) oldJson = "{}";
            _stagedDeletes.Add(RouteOp.Delete(nodePath, oldJson, $"delete {HandlerLabelOf(ed)}"));
        }

        // Remove the handler tab + all its sub-tabs (editors whose ConfigPath is under nodePath).
        // Walk descending so RemoveTab index shifts don't skip entries.
        var prefix = nodePath + "/";
        for (var i = _editors.Count - 1; i >= 0; i--)
        {
            var p = _editors[i].ConfigPath;
            if (p == nodePath || p.StartsWith(prefix, StringComparison.Ordinal))
            {
                _editors.RemoveAt(i);
                _tabControl?.RemoveTab(i);
            }
        }
        MarkDirty();
        SetStatus($"[{UIConstants.MutedText.ToMarkup()}]Staged delete (Ctrl+S to apply).[/]");
    }

    // From any of a handler's (sub-)editor ConfigPaths, find the handler NODE path: the path
    // up to and including the LAST "/handle/{N}" segment. Returns null if there is none.
    private static string? HandlerNodePath(string configPath)
    {
        const string marker = "/handle/";
        int searchEnd = configPath.Length;
        while (true)
        {
            var at = configPath.LastIndexOf(marker, searchEnd - 1, StringComparison.Ordinal);
            if (at < 0) return null;
            var idxStart = at + marker.Length;
            var end = idxStart;
            while (end < configPath.Length && char.IsDigit(configPath[end])) end++;
            if (end > idxStart) return configPath[..end]; // "{...}/handle/{N}"
            searchEnd = at; // this "/handle/" wasn't followed by digits; look earlier
            if (searchEnd <= 0) return null;
        }
    }

    private static string HandlerLabelOf(IConfigEditor ed)
        => ed switch
        {
            ReverseProxyEditor => "reverse_proxy",
            FileServerEditor => "file_server",
            _ => ed.TabTitle,
        };

    // === Apply: collect ALL ops and run as one mixed batch =========================
    private async Task ApplyAllAsync()
    {
        var ops = new List<RouteOp>();
        ops.AddRange(_stagedDeletes);
        foreach (var (ed, arr, type) in _stagedAdds)
        {
            // A staged-add is always a single valid whole-handler POST (the placeholder tab is
            // read-only); configuration of the new handler happens after Apply re-parses.
            ops.Add(RouteOp.Add(arr, NewRouteSkeleton.MinimalHandler(type), $"add {type}"));
        }
        foreach (var ed in _editors)
        {
            if (_addEditors.Contains(ed)) continue; // staged-adds handled above
            foreach (var pw in ed.BuildPatch()) ops.Add(RouteOp.Field(pw));
        }

        if (ops.Count == 0)
        {
            SetStatus($"[{UIConstants.MutedText.ToMarkup()}]No changes to apply.[/]");
            return;
        }

        var (oldCombined, newCombined) = BuildCombinedDiff(ops);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply route changes", oldCombined, newCombined, Modal))
            return;

        var res = await _editor.ApplyOpsAsync(ops, $"edit route {_route.HostOrMatch}");
        if (res.AllSucceeded)
        {
            SetStatus($"[{UIConstants.Good.ToMarkup()}]Applied {res.Applied} change(s).[/]");
            await RebuildTabsAsync(); // re-fetch the route + rebuild every tab so indices/sub-tabs are correct
        }
        else
        {
            SetStatus($"[{UIConstants.Bad.ToMarkup()}]Applied {res.Applied} of {res.Total}; failed on {Escape(res.FailedLabel ?? "?")}: {Escape(res.Error ?? "write failed")}[/]");
        }
    }

    // After a fully-successful apply: clear staged state, re-fetch the route JSON, and rebuild
    // every tab from scratch so handler indices and the now-existing sub-tabs are correct.
    private async Task RebuildTabsAsync()
    {
        _stagedAdds.Clear();
        _stagedDeletes.Clear();
        _addEditors.Clear();

        try { _routeJson = await _editor.GetConfigNodeAsync(_route.ConfigPath); }
        catch { /* keep last-known _routeJson */ }
        _primaryHandlerArray = ResolvePrimaryHandlerArray();

        if (_tabControl is not null) _tabControl.ClearTabs();
        _editors.Clear();

        _editors.AddRange(BuildEditors());
        if (_tabControl is not null)
        {
            foreach (var ed in _editors)
            {
                var panel = Controls.ScrollablePanel().Build();
                if (ed is ReverseProxyEditor rp) rp.OnError = ShowError;
                ed.Build(panel, MarkDirty);
                _tabControl.AddTab(ed.TabTitle, panel);
            }
            if (_editors.Count > 0) _tabControl.ActiveTabIndex = 0;
        }
        await LoadAllAsync();
    }

    // Assemble one combined before/after JSON object keyed by each op's Label (suffixed when a
    // label repeats). Delete → new = "(removed)"; Add → old = "(new)"; Field → old/new JSON.
    private static (string oldJson, string newJson) BuildCombinedDiff(IReadOnlyList<RouteOp> ops)
    {
        var oldObj = new Dictionary<string, JsonElement>();
        var newObj = new Dictionary<string, JsonElement>();
        var seen = new Dictionary<string, int>();
        foreach (var o in ops)
        {
            var key = o.Label;
            if (seen.TryGetValue(o.Label, out var n)) { n++; seen[o.Label] = n; key = $"{o.Label} ({n})"; }
            else seen[o.Label] = 0;

            switch (o.Kind)
            {
                case RouteOpKind.Delete:
                    oldObj[key] = Parse(o.OldJson);
                    newObj[key] = ParseLiteral("\"(removed)\"");
                    break;
                case RouteOpKind.Add:
                    oldObj[key] = ParseLiteral("\"(new)\"");
                    newObj[key] = Parse(o.Json);
                    break;
                default: // Field
                    oldObj[key] = Parse(o.OldJson);
                    newObj[key] = Parse(o.Json);
                    break;
            }
        }
        var opt = new JsonSerializerOptions { WriteIndented = true };
        return (JsonSerializer.Serialize(oldObj, opt), JsonSerializer.Serialize(newObj, opt));
    }

    private static JsonElement Parse(string json)
    {
        try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); return d.RootElement.Clone(); }
        catch { using var d = JsonDocument.Parse("{}"); return d.RootElement.Clone(); }
    }

    private static JsonElement ParseLiteral(string json)
    {
        using var d = JsonDocument.Parse(json); return d.RootElement.Clone();
    }

    // Reflect dirtiness + staged state into tab titles (trailing " *"). Staged-add tabs are
    // always marked. (Editors don't wire per-keystroke events, so markers refresh on
    // load/apply/structural-change rather than live — pre-existing, out of scope.)
    private void MarkDirty()
    {
        if (_tabControl is null) return;
        for (var i = 0; i < _editors.Count && i < _tabControl.TabCount; i++)
        {
            var ed = _editors[i];
            var dirty = ed.IsDirty || _addEditors.Contains(ed);
            _tabControl.SetTabTitle(i, dirty ? ed.TabTitle + " *" : ed.TabTitle);
        }
    }

    private void ShowError(string m) => SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]");

    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
