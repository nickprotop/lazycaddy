// -----------------------------------------------------------------------
// LazyCaddy - route editor: edit the route's match + browse/edit its handler
// chain. Each handler opens a type-specific form, or the raw node editor.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;
using LazyCaddy.UI.Modals.Handlers;

namespace LazyCaddy.UI.Modals;

public sealed class RouteEditorDialog : ModalBase<bool>
{
    private readonly Route _route;
    private readonly EditCoordinator _editor;
    private TableControl? _handlers;
    private MarkupControl? _hint;

    private RouteEditorDialog(Route route, EditCoordinator editor) { _route = route; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new RouteEditorDialog(route, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Route — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (86, 24);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        var accent = UIConstants.Accent.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]{Escape(_route.HostOrMatch)}[/]")
            .AddLine($"[{muted}]Handlers run top-to-bottom. Enter: edit · j: raw JSON · m: edit match.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _handlers = Controls.Table()
            .AddColumn("Handler", TextJustification.Left, 18)
            .AddColumn("Detail", TextJustification.Left)
            .Rounded().WithBorderColor(UIConstants.MutedText).Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithName("handlersTable").Build();
        _handlers.RowActivatedAsync += async (_, _) => await EditSelectedAsync(raw: false);
        Modal.AddControl(_handlers);

        _hint = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_hint);

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var routeJson = await _editor.GetConfigNodeAsync(_route.ConfigPath);
            var descriptors = RouteModel.ParseHandlers(routeJson, _route.ConfigPath);
            if (_handlers is null) return;
            _handlers.ClearRows();
            foreach (var d in descriptors)
            {
                var info = HandlerCatalog.Lookup(d.Type);
                var indent = new string(' ', d.Depth * 2);
                _handlers.AddRow(new TableRow($"{indent}{info.Icon} {info.DisplayName}", Escape(d.Summary)) { Tag = d });
            }
            if (descriptors.Count > 0) _handlers.SelectedRowIndex = 0;
        }
        catch (Exception ex) { _hint?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{Escape(ex.Message)}[/]" }); }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.J) { e.Handled = true; _ = EditSelectedAsync(raw: true); return; }
        if (e.KeyInfo.Key == ConsoleKey.M) { e.Handled = true; _ = EditMatchAsync(); }
    }

    private async Task EditSelectedAsync(bool raw)
    {
        if (_handlers?.SelectedRow?.Tag is not HandlerDescriptor d) return;
        bool changed;
        if (raw)
            changed = await RawNodeEditDialog.ShowAsync(WindowSystem, $"Edit {d.Type}", d.ConfigPath, _editor, Modal);
        else
            changed = await OpenFormFor(d);
        if (changed) await LoadAsync(); // refresh summaries after an edit
    }

    private Task<bool> OpenFormFor(HandlerDescriptor d) => d.Type switch
    {
        "file_server"     => FileServerForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        "static_response" => StaticResponseForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        "error"           => ErrorForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        // No dedicated form yet (reverse_proxy/subroute/middleware) → raw node editor.
        _                 => RawNodeEditDialog.ShowAsync(WindowSystem, $"Edit {d.Type}", d.ConfigPath, _editor, Modal),
    };

    private async Task EditMatchAsync()
    {
        // Reuse the existing combined route dialog's match editing by opening the raw
        // match node (host/path) — a structured match form is covered by EditRouteDialog
        // for reverse_proxy routes; here the raw node guarantees match editability too.
        var changed = await RawNodeEditDialog.ShowAsync(WindowSystem, "Edit match", $"{_route.ConfigPath}/match", _editor, Modal);
        if (changed) await LoadAsync();
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
