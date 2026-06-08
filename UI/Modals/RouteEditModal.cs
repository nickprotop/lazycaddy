// -----------------------------------------------------------------------
// LazyCaddy - the consolidated route edit modal: one modal, a tab strip of
// IConfigEditors, ONE combined diff, and a single batched apply. For CM-3 it
// hosts just the reverse_proxy HealthChecks editor to prove the path end-to-end;
// CM-4 assembles the full tab set.
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

    private RouteEditModal(Route route, EditCoordinator editor) { _route = route; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new RouteEditModal(route, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Edit route — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (96, 32);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();

        // CM-4: assemble the full ordered tab set from the route's handler chain.
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
                // Editors that write outside the batch (ReverseProxy's single-upstream delete) report
                // failures to the modal status line.
                if (ed is LazyCaddy.UI.Editors.ReverseProxyEditor rp) rp.OnError = ShowError;
                ed.Build(panel, MarkDirty);
                tabs.AddTab(ed.TabTitle, panel);
            }
            tabs.WithActiveTab(0);
            _tabControl = tabs.Build();
            Modal.AddControl(_tabControl);
        }

        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Ctrl+S: apply all   Esc: close[/]")
            .WithMargin(2, 0, 2, 0).StickyBottom().Build());
        _status = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_status);

        RunGuarded(LoadAllAsync, ShowError);
    }

    // Assemble the ordered editor (tab) list for the route:
    //   Match, then for each TOP-LEVEL (Depth==0) handler in order, that handler's
    //   editor tab(s). reverse_proxy and file_server expand into several peer tabs.
    //   subroute and unknown handler types are skipped (nested editing is out of scope).
    //   Multiple top-level handlers => their tabs are flattened in handler order;
    //   colliding tab titles (e.g. two "Headers") are acceptable for now.
    private List<IConfigEditor> BuildEditors()
    {
        var list = new List<IConfigEditor>();
        // 1. Always-first Match tab.
        list.Add(new MatchEditor(_route.ConfigPath));

        IReadOnlyList<HandlerDescriptor> descriptors;
        try { descriptors = RouteModel.ParseHandlers(_route.RawConfigJson, _route.ConfigPath); }
        catch { return list; }

        // Build a tab for EVERY real handler — top-level OR nested inside a subroute (the common
        // Caddyfile shape wraps a route's handlers in a subroute, so the reverse_proxy is at
        // Depth>0). ParseHandlers already yields each with its full nested ConfigPath, and the
        // editors are path-driven, so nested handlers' sub-node paths (e.g. {p}/health_checks,
        // {p}/transport/tls, {p}/upstreams/{i}) compose correctly. Only the subroute CONTAINER
        // itself has no editor — skip it; its nested handlers come through as their own descriptors.
        foreach (var d in descriptors)
        {
            if (d.Type == "subroute") continue; // container, not an editable leaf; its children follow
            var p = d.ConfigPath;
            switch (d.Type)
            {
                case "reverse_proxy":
                    // Peer tabs for the reverse_proxy node `p`.
                    list.Add(new ReverseProxyEditor(p));                       // "Upstreams"
                    list.Add(new LoadBalancingEditor(p));                      // "Load balancing"
                    list.Add(new HealthChecksEditor(p));                       // "Health checks"
                    list.Add(new HttpTransportEditor(p));                      // "Transport" (node {p}/transport)
                    list.Add(new TlsConfigEditor($"{p}/transport/tls"));      // "TLS" (full path, no append)
                    list.Add(new KeepAliveEditor($"{p}/transport"));          // "Keep-alive" (editor appends /keep_alive)
                    list.Add(new HeadersEditor($"{p}/headers"));              // "Headers"
                    break;
                case "file_server":
                    list.Add(new FileServerEditor(p));                         // "File server"
                    list.Add(new BrowseEditor(p));                             // "Browse" (editor appends /browse)
                    break;
                case "static_response":
                    list.Add(new StaticResponseEditor(p));
                    break;
                case "error":
                    list.Add(new ErrorEditor(p));
                    break;
                case "rewrite":
                    list.Add(new RewriteEditor(p));
                    break;
                case "headers":
                    list.Add(new HeadersEditor(p));
                    break;
                case "encode":
                    list.Add(new EncodeEditor(p));
                    break;
                case "vars":
                    list.Add(new VarsEditor(p));
                    break;
                case "request_body":
                    list.Add(new RequestBodyEditor(p));
                    break;
                case "templates":
                    list.Add(new TemplatesEditor(p));
                    break;
                case "authentication":
                    list.Add(new AuthenticationEditor(p));                     // editor appends /providers
                    break;
                // unknown handler types: skip (don't crash). subroute is filtered above.
                default:
                    break;
            }
        }
        return list;
    }

    private async Task LoadAllAsync()
    {
        foreach (var e in _editors)
            await e.LoadAsync(_editor);
        MarkDirty(); // refresh tab markers against the freshly-loaded (clean) baseline
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        // Ctrl+S → apply all tabs in one batch.
        if (e.KeyInfo.Key == ConsoleKey.S && (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0)
        {
            e.Handled = true;
            RunGuarded(ApplyAllAsync, ShowError);
            return;
        }
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            e.Handled = true;
            if (_editors.Any(ed => ed.IsDirty))
            {
                RunGuarded(async () =>
                {
                    var choice = await UnsavedChangesDialog.ShowAsync(WindowSystem, Modal);
                    if (choice == UnsavedChoice.Apply)
                    {
                        await ApplyAllAsync();
                        if (!_editors.Any(ed => ed.IsDirty)) CloseWithResult(true);
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

    private IConfigEditor? ActiveEditor
    {
        get
        {
            if (_tabControl is null || _editors.Count == 0) return null;
            var i = _tabControl.ActiveTabIndex;
            return i >= 0 && i < _editors.Count ? _editors[i] : null;
        }
    }

    private async Task ApplyAllAsync()
    {
        var writes = _editors.SelectMany(e => e.BuildPatch()).ToList();
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
            // Re-load so editors capture the new baseline (dirty resets).
            await LoadAllAsync();
        }
        else
        {
            SetStatus($"[{UIConstants.Bad.ToMarkup()}]Applied {res.Applied} of {res.Total}; failed on {Escape(res.FailedLabel ?? "?")}: {Escape(res.Error ?? "write failed")}[/]");
        }
    }

    // Assemble one combined before/after JSON object keyed by each write's Label
    // (suffixed when a label repeats), so the diff dialog shows every tab's change.
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

    // The onDirtyChanged callback: reflect each editor's current IsDirty into its tab
    // title (trailing " *" marker). Editors call this after LoadAsync (all clean), and
    // the modal calls it after LoadAllAsync / a successful apply so markers clear.
    // Note: editors don't wire per-keystroke change events, so markers refresh on
    // load/apply/explicit refresh — not live per-keystroke (pre-existing, out of scope).
    private void MarkDirty()
    {
        if (_tabControl is null) return;
        for (var i = 0; i < _editors.Count; i++)
        {
            var ed = _editors[i];
            _tabControl.SetTabTitle(i, ed.IsDirty ? ed.TabTitle + " *" : ed.TabTitle);
        }
    }

    private void ShowError(string m) => SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]");

    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
