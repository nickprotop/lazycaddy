using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class VarsForm : ModalBase<bool>
{
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private MultilineEditControl? _edit;
    private MarkupControl? _error;
    private string _original = "{}";

    private VarsForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new VarsForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit vars ";
    protected override (int width, int height) GetSize() => (72, 18);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]One variable per line: name = value. Ctrl+S apply · Esc cancel.[/]").WithMargin(2, 1, 2, 0).Build());
        _edit = Controls.MultilineEdit("").WithVerticalAlignment(VerticalAlignment.Fill).NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 1, 2, 0).Build();
        Modal.AddControl(_edit);
        _error = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build(); Modal.AddControl(_error);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original);
            var lines = new List<string>();
            foreach (var p in d.RootElement.EnumerateObject())
                if (p.Name != "handler")
                    lines.Add($"{p.Name} = {(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())}");
            _edit?.SetContent(string.Join("\n", lines));
        }
        catch (JsonException ex) { Err($"Could not parse vars node: {ex.Message}"); }
        catch { }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.S && (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0) { e.Handled = true; _ = ApplyAsync(); }
    }

    private async Task ApplyAsync()
    {
        var entries = (_edit?.GetContent() ?? "").Split('\n')
            .Select(l => l.Split('=', 2))
            .Where(kv => kv.Length == 2 && kv[0].Trim().Length > 0)
            .Select(kv => (kv[0].Trim(), kv[1].Trim()))
            .ToArray();
        var newJson = HandlerPatch.Vars(entries);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply vars", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.PatchConfigAsync(_path, newJson, ct), $"vars {_path}");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
