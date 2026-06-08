// -----------------------------------------------------------------------
// LazyCaddy - generic single-field editor. PATCHes a raw JSON value at a
// config path, going through DiffConfirmDialog + EditCoordinator to apply.
// Reusable building block for nodes that don't warrant a bespoke dialog.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

/// <summary>Generic single-field editor: PATCHes a raw JSON value at a config path.</summary>
public sealed class EditFieldDialog : ModalBase<bool>
{
    private readonly string _label, _path, _current;
    private readonly EditCoordinator _editor;
    private PromptControl? _input;
    private MarkupControl? _error;

    private EditFieldDialog(string label, string path, string current, EditCoordinator editor)
    { _label = label; _path = path; _current = current; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string label, string path, string current, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new EditFieldDialog(label, path, current, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Edit — {_label} ";
    protected override (int width, int height) GetSize() => (72, 11);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter a JSON value for {_path}[/]").WithMargin(2, 1, 2, 0).Build());
        _input = Controls.Prompt($"{_label}: ").WithInput(_current).WithInputWidth(48).Build();
        Modal.AddControl(_input);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, m => _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" })); }
    }

    private async Task ApplyAsync()
    {
        var newJson = (_input?.Input ?? "").Trim();
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, $"Apply {_label}", _current, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), $"{_label} → {newJson}");
        if (result.Success) CloseWithResult(true);
        else _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{(result.Error ?? "").Replace("[", "[[").Replace("]", "]]")}[/]" });
    }
}
