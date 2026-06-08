// -----------------------------------------------------------------------
// LazyCaddy - structured editor for a file_server handler node.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class FileServerForm : ModalBase<bool>
{
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private PromptControl? _root, _index, _hide;
    private CheckboxControl? _browse, _passThru;
    private MarkupControl? _error;
    private string _original = "{}";

    private FileServerForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new FileServerForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit file_server ";
    protected override (int width, int height) GetSize() => (74, 15);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Static file serving. Comma-separated lists.[/]").WithMargin(2, 1, 2, 0).Build());
        _root = Controls.Prompt("Root:        ").WithInputWidth(48).Build();
        _index = Controls.Prompt("Index names: ").WithInputWidth(48).Build();
        _hide = Controls.Prompt("Hide:        ").WithInputWidth(48).Build();
        _browse = new CheckboxControl { Label = "Browse (directory listing)", Checked = false };
        _passThru = new CheckboxControl { Label = "Pass through on 404", Checked = false };
        Modal.AddControl(_root); Modal.AddControl(_index); Modal.AddControl(_hide);
        Modal.AddControl(_browse); Modal.AddControl(_passThru);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original);
            var r = d.RootElement;
            if (r.TryGetProperty("root", out var root) && root.ValueKind == JsonValueKind.String) _root?.SetInput(root.GetString());
            _index?.SetInput(JoinArr(r, "index_names"));
            _hide?.SetInput(JoinArr(r, "hide"));
            if (_browse is not null) _browse.Checked = r.TryGetProperty("browse", out _);
            if (_passThru is not null) _passThru.Checked = r.TryGetProperty("pass_thru", out var pt) && pt.ValueKind == JsonValueKind.True;
        }
        catch { /* leave defaults */ }
    }

    private static string JoinArr(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array
            ? string.Join(", ", a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()))
            : "";

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; _ = ApplyAsync(); }
    }

    private async Task ApplyAsync()
    {
        var newJson = HandlerPatch.FileServer(
            (_root?.Input ?? "").Trim(),
            Split(_index?.Input), Split(_hide?.Input),
            _browse?.Checked ?? false, _passThru?.Checked ?? false);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply file_server", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.PatchConfigAsync(_path, newJson, ct), $"file_server {_path}");
        if (result.Success) CloseWithResult(true);
        else _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{(result.Error ?? "").Replace("[", "[[").Replace("]", "]]")}[/]" });
    }

    private static string[] Split(string? s) =>
        (s ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
