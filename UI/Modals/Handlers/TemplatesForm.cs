using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class TemplatesForm : ModalBase<bool>
{
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private PromptControl? _fileRoot, _mime;
    private MarkupControl? _error;
    private string _original = "{}";

    private TemplatesForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new TemplatesForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit templates ";
    protected override (int width, int height) GetSize() => (74, 12);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Server-side templating.[/]").WithMargin(2, 1, 2, 0).Build());
        _fileRoot = Controls.Prompt("File root:  ").WithInputWidth(46).Build();
        _mime = Controls.Prompt("MIME types: ").WithInputWidth(46).Build();
        Modal.AddControl(_fileRoot); Modal.AddControl(_mime);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.TryGetProperty("file_root", out var fr) && fr.ValueKind == JsonValueKind.String) _fileRoot?.SetInput(fr.GetString());
            if (r.TryGetProperty("mime_types", out var mt) && mt.ValueKind == JsonValueKind.Array)
                _mime?.SetInput(string.Join(", ", mt.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString())));
        }
        catch (JsonException ex) { Err($"Could not parse templates node: {ex.Message}"); }
        catch { }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; _ = ApplyAsync(); }
    }

    private async Task ApplyAsync()
    {
        var mime = (_mime?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var newJson = HandlerPatch.Templates((_fileRoot?.Input ?? "").Trim(), mime);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply templates", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), $"templates {_path}");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
