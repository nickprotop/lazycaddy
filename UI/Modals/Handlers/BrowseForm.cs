using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

// Edits a file_server `browse` sub-node (directory-listing options).
public sealed class BrowseForm : ModalBase<bool>
{
    private readonly string _path;   // = "{fileServerPath}/browse"
    private readonly EditCoordinator _editor;
    private PromptControl? _template, _sort, _fileLimit;
    private CheckboxControl? _revealSymlinks;
    private MarkupControl? _error;
    private string _original = "{}";

    private BrowseForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new BrowseForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Browse (directory listing) ";
    protected override (int width, int height) GetSize() => (76, 14);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Directory listing options. Sort: comma list e.g. 'name,asc' or 'time,desc'.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _template = Controls.Prompt("Template file: ").WithInputWidth(44).Build();
        _sort = Controls.Prompt("Sort:          ").WithInputWidth(44).Build();
        _fileLimit = Controls.Prompt("File limit:    ").WithInput("0").WithInputWidth(12).Build();
        _revealSymlinks = new CheckboxControl { Label = "reveal_symlinks (show symlink targets)", Checked = false };
        Modal.AddControl(_template); Modal.AddControl(_sort); Modal.AddControl(_fileLimit); Modal.AddControl(_revealSymlinks);
        _error = Controls.Markup().WithMargin(2, 0, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
        RunGuarded(LoadAsync, Err);
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return;
            if (r.TryGetProperty("template_file", out var tf) && tf.ValueKind == JsonValueKind.String) _template?.SetInput(tf.GetString());
            if (r.TryGetProperty("reveal_symlinks", out var rs) && rs.ValueKind == JsonValueKind.True) _revealSymlinks!.Checked = true;
            if (r.TryGetProperty("sort", out var so) && so.ValueKind == JsonValueKind.Array)
                _sort?.SetInput(string.Join(", ", so.EnumerateArray().Select(e => e.ToString())));
            if (r.TryGetProperty("file_limit", out var fl) && fl.ValueKind == JsonValueKind.Number) _fileLimit?.SetInput(fl.ToString());
        }
        catch (JsonException ex) { Err($"Could not parse browse node: {ex.Message}"); }
        catch { }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private async Task ApplyAsync()
    {
        int.TryParse((_fileLimit?.Input ?? "0").Trim(), out var fileLimit);
        var sort = (_sort?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var input = new BrowseInput((_template?.Input ?? "").Trim(), _revealSymlinks?.Checked ?? false, sort, fileLimit);
        var newJson = HandlerPatch.Browse(input);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply browse", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), "file_server browse");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
