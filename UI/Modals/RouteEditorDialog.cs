// -----------------------------------------------------------------------
// LazyCaddy - route editor: edit the route's match + browse/edit its handler
// chain. Each handler opens a type-specific form, or the raw node editor.
// -----------------------------------------------------------------------

using System.Text.Json;
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
        // NOTE: RowActivatedAsync is awaited by the framework; the keyboard launches below
        // are fire-and-forget, so they go through RunGuarded to surface errors (see OnKeyPressed).
        Modal.AddControl(_handlers);

        _hint = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_hint);

        RunGuarded(LoadAsync, ShowHintError);
    }

    private void ShowHintError(string m) =>
        _hint?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]" });

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
        if (e.KeyInfo.Key == ConsoleKey.J) { e.Handled = true; RunGuarded(() => EditSelectedAsync(raw: true), ShowHintError); return; }
        if (e.KeyInfo.Key == ConsoleKey.M) { e.Handled = true; RunGuarded(EditMatchAsync, ShowHintError); }
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
        "rewrite"         => RewriteForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        "headers"         => HeadersForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        "encode"          => EncodeForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        "vars"            => VarsForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        "request_body"    => RequestBodyForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        "reverse_proxy"   => ReverseProxyForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        "templates"       => TemplatesForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        "authentication"  => AuthenticationForm.ShowAsync(WindowSystem, d.ConfigPath, _editor, Modal),
        "subroute"        => DrillIntoSubrouteAsync(d),
        _                 => RawNodeEditDialog.ShowAsync(WindowSystem, $"Edit {d.Type}", d.ConfigPath, _editor, Modal),
    };

    private async Task<bool> DrillIntoSubrouteAsync(HandlerDescriptor d)
    {
        // Read the subroute's nested routes and let the user pick one to edit.
        string subJson;
        try { subJson = await _editor.GetConfigNodeAsync($"{d.ConfigPath}/routes"); }
        catch { return await RawNodeEditDialog.ShowAsync(WindowSystem, "Edit subroute", d.ConfigPath, _editor, Modal); }

        using var doc = JsonDocument.Parse(subJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            return await RawNodeEditDialog.ShowAsync(WindowSystem, "Edit subroute", d.ConfigPath, _editor, Modal);

        // For each nested route, derive a display label + its config path, and open a picker.
        var children = new List<Route>();
        int n = 0;
        foreach (var rn in doc.RootElement.EnumerateArray())
        {
            var label = NestedRouteLabel(rn, n);
            children.Add(new Route(label, "", false, "active", "{}", $"{d.ConfigPath}/routes/{n}"));
            n++;
        }
        // If exactly one nested route, drill straight in; else show a small picker dialog.
        var pick = children.Count == 1 ? children[0]
            : await SubroutePickerDialog.ShowPickAsync(WindowSystem, children, Modal);
        if (pick is null) return false;
        return await RouteEditorDialog.ShowAsync(WindowSystem, pick, _editor, Modal);
    }

    private static string NestedRouteLabel(JsonElement route, int idx)
    {
        if (route.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() > 0
            && m[0].TryGetProperty("host", out var h) && h.ValueKind == JsonValueKind.Array && h.GetArrayLength() > 0)
            return h[0].GetString() ?? $"route {idx}";
        return $"route {idx}";
    }

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
