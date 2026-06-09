// -----------------------------------------------------------------------
// LazyCaddy - the consolidated route edit modal: one modal, a tab strip of
// IConfigEditors (Match + each handler's settings), ONE combined diff, and a
// single batched apply (EditCoordinator.ApplyBatchAsync).
//
// This modal EDITS the existing handlers' fields only. Adding/deleting handlers
// is done from the Routes page, not here — keeping the modal focused on field edits.
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

    // The route node JSON the editors are built from. Init'd from the route's captured
    // JSON; re-fetched live on RebuildTabs after a successful apply.
    private string _routeJson;

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
                if (ed is ReverseProxyEditor rp) { rp.OnError = ShowError; rp.DialPrompt = (title, initial) => UpstreamDialog.ShowAsync(WindowSystem, title, initial, Modal); rp.RequestRelayout = () => Modal.ForceRebuildLayout(); rp.ConfirmRemove = what => ConfirmDeleteDialog.ShowAsync(WindowSystem, what, Modal); }
                ed.Build(panel, MarkDirty);
                tabs.AddTab(ed.TabTitle, panel);
            }
            tabs.WithActiveTab(0);
            _tabControl = tabs.Build();
            Modal.AddControl(_tabControl);
        }

        _status = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_status);

        Modal.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Apply", "Ctrl+S", () => RunGuarded(ApplyAllAsync, ShowError)))
            .AddButton(UIConstants.ActionButton("Close", "Esc", () => RunGuarded(CloseInteractiveAsync, ShowError)))
            .WithSpacing(2).WithMargin(2, 0, 2, 0).WithAboveLine().StickyBottom().Build());

        RunGuarded(LoadAllAsync, ShowError);
    }

    // Assemble the ordered editor (tab) list for the route: Match first, then every real
    // handler's editor tab(s) from _routeJson. Subroute containers are skipped (their
    // children compose their own ConfigPaths and follow in order).
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

    private async Task LoadAllAsync()
    {
        foreach (var e in _editors)
            await e.LoadAsync(_editor);
        MarkDirty(); // refresh tab markers against the freshly-loaded (clean) baseline
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0;

        // Ctrl+S → apply all field edits in one batch.
        if (e.KeyInfo.Key == ConsoleKey.S && ctrl)
        {
            e.Handled = true;
            RunGuarded(ApplyAllAsync, ShowError);
            return;
        }
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            e.Handled = true;
            RunGuarded(CloseInteractiveAsync, ShowError);
            return;
        }
        // Otherwise route to the active tab's editor.
        if (ActiveEditor?.HandleKey(e.KeyInfo) == true)
            e.Handled = true;
    }

    private async Task CloseInteractiveAsync()
    {
        if (!AnyPending()) { CloseWithResult(false); return; }
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
    }

    private bool AnyPending() => _editors.Any(ed => ed.IsDirty);

    private IConfigEditor? ActiveEditor
    {
        get
        {
            if (_tabControl is null || _editors.Count == 0) return null;
            var i = _tabControl.ActiveTabIndex;
            return i >= 0 && i < _editors.Count ? _editors[i] : null;
        }
    }

    // === Apply: collect each editor's field writes and run as one batch =============
    private async Task ApplyAllAsync()
    {
        var writes = new List<PendingWrite>();
        foreach (var ed in _editors)
            writes.AddRange(ed.BuildPatch());

        if (writes.Count == 0)
        {
            SetStatus($"[{UIConstants.MutedText.ToMarkup()}]No changes to apply.[/]");
            return;
        }

        var (oldCombined, newCombined) = BuildCombinedDiff(writes);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply route changes", oldCombined, newCombined, Modal))
            return;

        var res = await _editor.ApplyBatchAsync(writes, $"edit route {_route.HostOrMatch}");
        if (res.AllSucceeded)
        {
            SetStatus($"[{UIConstants.Good.ToMarkup()}]Applied {res.Applied} change(s).[/]");
            await RebuildTabsAsync(); // re-fetch the route + rebuild every tab so values reflect the new state
        }
        else
        {
            SetStatus($"[{UIConstants.Bad.ToMarkup()}]Applied {res.Applied} of {res.Total}; failed on {Escape(res.FailedLabel ?? "?")}: {Escape(CaddyErrorFormatter.Format(res.Error))}[/]");
        }
    }

    // After a fully-successful apply: re-fetch the route JSON and rebuild every tab from
    // scratch so editor values reflect the now-current config.
    private async Task RebuildTabsAsync()
    {
        try { _routeJson = await _editor.GetConfigNodeAsync(_route.ConfigPath); }
        catch { /* keep last-known _routeJson */ }

        if (_tabControl is not null) _tabControl.ClearTabs();
        _editors.Clear();

        _editors.AddRange(BuildEditors());
        if (_tabControl is not null)
        {
            foreach (var ed in _editors)
            {
                var panel = Controls.ScrollablePanel().Build();
                if (ed is ReverseProxyEditor rp) { rp.OnError = ShowError; rp.DialPrompt = (title, initial) => UpstreamDialog.ShowAsync(WindowSystem, title, initial, Modal); rp.RequestRelayout = () => Modal.ForceRebuildLayout(); rp.ConfirmRemove = what => ConfirmDeleteDialog.ShowAsync(WindowSystem, what, Modal); }
                ed.Build(panel, MarkDirty);
                _tabControl.AddTab(ed.TabTitle, panel);
            }
            if (_editors.Count > 0) _tabControl.ActiveTabIndex = 0;
        }
        await LoadAllAsync();
    }

    // Assemble one combined before/after JSON object keyed by each write's Label (suffixed
    // when a label repeats).
    private static (string oldJson, string newJson) BuildCombinedDiff(IReadOnlyList<PendingWrite> writes)
    {
        var oldObj = new Dictionary<string, JsonElement>();
        var newObj = new Dictionary<string, JsonElement>();
        var seen = new Dictionary<string, int>();
        foreach (var w in writes)
        {
            var key = w.Label;
            if (seen.TryGetValue(w.Label, out var n)) { n++; seen[w.Label] = n; key = $"{w.Label} ({n})"; }
            else seen[w.Label] = 0;

            oldObj[key] = Parse(w.OldJson);
            newObj[key] = Parse(w.Json);
        }
        var opt = new JsonSerializerOptions { WriteIndented = true };
        return (JsonSerializer.Serialize(oldObj, opt), JsonSerializer.Serialize(newObj, opt));
    }

    private static JsonElement Parse(string json)
    {
        try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); return d.RootElement.Clone(); }
        catch { using var d = JsonDocument.Parse("{}"); return d.RootElement.Clone(); }
    }

    // Reflect dirtiness into tab titles (trailing " *"). (Editors don't wire per-keystroke
    // events, so markers refresh on load/apply rather than live — pre-existing, out of scope.)
    private void MarkDirty()
    {
        if (_tabControl is null) return;
        for (var i = 0; i < _editors.Count && i < _tabControl.TabCount; i++)
        {
            var ed = _editors[i];
            _tabControl.SetTabTitle(i, ed.IsDirty ? ed.TabTitle + " *" : ed.TabTitle);
        }
    }

    private void ShowError(string m) => SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]");

    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
