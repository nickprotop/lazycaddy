// -----------------------------------------------------------------------
// LazyCaddy - universal fallback: edit ANY config node as raw JSON.
// Reads the node, lets the user edit it, diff/confirm, then PATCH. Guarantees
// every handler/route is editable even without a dedicated form.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class RawNodeEditDialog : ModalBase<bool>
{
    private readonly string _title;
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private MultilineEditControl? _edit;
    private MarkupControl? _error;
    private string _original = "";

    private RawNodeEditDialog(string title, string path, EditCoordinator editor)
    { _title = title; _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string title, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new RawNodeEditDialog(title, path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" {_title} ";
    protected override (int width, int height) GetSize() => (90, 30);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Raw JSON for {_path}. Ctrl+S apply · Esc cancel.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _edit = Controls.MultilineEdit("")
            .WithLineNumbers(true)
            .WithSyntaxHighlighter(new JsonSyntaxHighlighter())
            .WithVerticalAlignment(VerticalAlignment.Fill).NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 1, 2, 0).Build();
        Modal.AddControl(_edit);

        _error = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_error);

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var doc = System.Text.Json.JsonDocument.Parse(_original);
            _original = System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            _edit?.SetContent(_original);
        }
        catch (Exception ex) { ShowError($"Could not read node: {ex.Message}"); }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.S && (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0)
        {
            e.Handled = true;
            _ = ApplyAsync();
        }
    }

    private async Task ApplyAsync()
    {
        if (_edit is null) return;
        var newJson = _edit.GetContent();
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, $"Apply {_path}", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), $"raw edit {_path}");
        if (result.Success) CloseWithResult(true);
        else ShowError(result.Error ?? "Write failed.");
    }

    private void ShowError(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
