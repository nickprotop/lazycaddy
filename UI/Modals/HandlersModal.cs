// -----------------------------------------------------------------------
// LazyCaddy - the route's handler chain manager: list this route's handlers,
// add a new one (type picker → POST a minimal handler), or delete one
// (confirm → DELETE the handler node). Field editing lives in RouteEditModal;
// this modal only adds/removes whole handlers.
//
// Add flow: pick type → POST NewRouteSkeleton.MinimalHandler(type) appended to
// the route's primary handler chain → close this modal and signal the caller to
// open RouteEditModal on the route so the new (minimal) handler gets configured.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class HandlersModal : ModalBase<bool>
{
    private readonly Route _route;
    private readonly EditCoordinator _editor;

    private TableControl? _table;
    private ButtonControl? _deleteBtn;
    private MarkupControl? _status;

    // Current route node JSON; re-fetched after each structural change so handler
    // indices / the list reflect the live shape.
    private string _routeJson;

    // Parallel to the table rows: the handler node path for each listed handler.
    private readonly List<HandlerDescriptor> _handlers = new();

    // True once an Add happened — the caller (RoutesView) then opens RouteEditModal
    // on the route so the new minimal handler can be configured.
    private bool _addedHandler;

    private HandlersModal(Route route, EditCoordinator editor)
    {
        _route = route; _editor = editor; _routeJson = route.RawConfigJson;
    }

    /// <summary>Show the handlers manager. Returns true if a handler was added (caller should
    /// then open RouteEditModal on the route to configure it).</summary>
    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new HandlersModal(route, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Handlers — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (76, 24);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]The handlers this route runs, in order. Add or remove whole handlers; edit their settings from the route editor.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _deleteBtn = UIConstants.ActionButton("Delete", "Del", () => _ = DeleteSelectedAsync());
        Modal.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Add", "Ctrl+A", () => _ = AddAsync()))
            .AddButton(_deleteBtn)
            .WithSpacing(2).WithMargin(2, 0, 2, 0).Build());

        _table = Controls.Table()
            .AddColumn("Type", TextJustification.Left, 20)
            .AddColumn("Summary", TextJustification.Left)
            .Rounded().WithBorderColor(UIConstants.MutedText).Interactive()
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithName("handlersTable").Build();
        _table.SelectedRowChanged += (_, _) => UpdateButtonStates();
        Modal.AddControl(_table);

        _status = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_status);

        Modal.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Close", "Esc", () => CloseWithResult(_addedHandler)))
            .WithMargin(2, 0, 2, 0).WithAboveLine().StickyBottom().Build());

        Refresh();
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0;
        if (e.KeyInfo.Key == ConsoleKey.A && ctrl) { e.Handled = true; _ = AddAsync(); return; }
        if ((e.KeyInfo.Key == ConsoleKey.Delete || e.KeyInfo.KeyChar == '-') && (_table?.HasFocus ?? false))
        { e.Handled = true; _ = DeleteSelectedAsync(); return; }
        if (e.KeyInfo.Key == ConsoleKey.Escape) { e.Handled = true; CloseWithResult(_addedHandler); }
    }

    // Repopulate the table from the current _routeJson. Skips subroute containers (they're
    // structural wrappers, not handlers the user adds/removes here); their leaf children show.
    private void Refresh()
    {
        if (_table is null) return;
        _handlers.Clear();
        try
        {
            foreach (var d in RouteModel.ParseHandlers(_routeJson, _route.ConfigPath))
                if (d.Type != "subroute") _handlers.Add(d);
        }
        catch { /* leave list empty on parse failure */ }

        var keep = _table.SelectedRowIndex;
        _table.ClearRows();
        foreach (var h in _handlers)
            _table.AddRow(new TableRow(Escape(h.Type), Escape(h.Summary)));
        _table.SelectedRowIndex = _handlers.Count == 0 ? -1 : Math.Clamp(keep < 0 ? 0 : keep, 0, _handlers.Count - 1);
        UpdateButtonStates();
        Modal.ForceRebuildLayout();
    }

    private void UpdateButtonStates()
    {
        var hasSel = (_table?.SelectedRowIndex ?? -1) >= 0 && _handlers.Count > 0;
        if (_deleteBtn is not null) _deleteBtn.IsEnabled = hasSel;
    }

    // === Add: pick a type, POST a minimal handler, then open the route editor =======
    private async Task AddAsync()
    {
        var type = await HandlerTypePicker.ShowAsync(WindowSystem, Modal);
        if (type is null) return;

        var arrayPath = ResolvePrimaryHandlerArray();
        var json = NewRouteSkeleton.MinimalHandler(type);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, $"Add {type} handler", "(new)", json, Modal))
            return;

        var res = await _editor.ApplyAsync(
            (admin, ct) => admin.PostConfigAsync(arrayPath, json, ct),
            $"add {type} handler to {_route.HostOrMatch}");
        if (!res.Success) { SetError(res.FriendlyError); return; }

        // The new handler is minimal — close and let the caller open RouteEditModal to configure it.
        _addedHandler = true;
        CloseWithResult(true);
    }

    // === Delete: confirm, then DELETE the handler node ==============================
    private async Task DeleteSelectedAsync()
    {
        var idx = _table?.SelectedRowIndex ?? -1;
        if (idx < 0 || idx >= _handlers.Count) return;
        var h = _handlers[idx];

        if (!await ConfirmDeleteDialog.ShowAsync(WindowSystem, $"{h.Type} handler", Modal)) return;

        var res = await _editor.ApplyAsync(
            (admin, ct) => admin.DeleteConfigAsync(h.ConfigPath, ct),
            $"delete {h.Type} handler from {_route.HostOrMatch}");
        if (!res.Success) { SetError(res.FriendlyError); return; }

        // Re-fetch the route node so indices/list reflect the removal, then refresh.
        try { _routeJson = await _editor.GetConfigNodeAsync(_route.ConfigPath); }
        catch { /* keep last-known _routeJson */ }
        Refresh();
        SetStatus($"[{UIConstants.Good.ToMarkup()}]Deleted {Escape(h.Type)} handler.[/]");
    }

    // The route's primary handler-chain array path: the FIRST real (non-subroute) handler's
    // ConfigPath minus its trailing "/{index}" segment (subroute-aware). Falls back to
    // "{route}/handle" for a route with no handlers yet.
    private string ResolvePrimaryHandlerArray()
    {
        try
        {
            var first = RouteModel.ParseHandlers(_routeJson, _route.ConfigPath)
                .FirstOrDefault(d => d.Type != "subroute");
            if (first is not null)
            {
                var i = first.ConfigPath.LastIndexOf('/');
                if (i > 0) return first.ConfigPath[..i];
            }
        }
        catch { /* fall through */ }
        return $"{_route.ConfigPath}/handle";
    }

    private void SetError(string m) => SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]");
    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });
    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
