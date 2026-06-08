using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class ErrorForm : ModalBase<bool>
{
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private PromptControl? _msg, _status;
    private MarkupControl? _error;
    private string _original = "{}";

    private ErrorForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new ErrorForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit error ";
    protected override (int width, int height) GetSize() => (74, 12);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Return an error (triggers error routes).[/]").WithMargin(2, 1, 2, 0).Build());
        _msg = Controls.Prompt("Message:     ").WithInputWidth(48).Build();
        _status = Controls.Prompt("Status code: ").WithInput("500").WithInputWidth(10).Build();
        Modal.AddControl(_msg); Modal.AddControl(_status);
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
            if (r.TryGetProperty("error", out var m) && m.ValueKind == JsonValueKind.String) _msg?.SetInput(m.GetString());
            if (r.TryGetProperty("status_code", out var s)) _status?.SetInput(s.ToString());
        }
        catch (JsonException ex)
        {
            _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]Could not parse error node: {ex.Message.Replace("[", "[[").Replace("]", "]]")}[/]" });
        }
        catch { /* node absent (404)/network → leave defaults */ }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; _ = ApplyAsync(); }
    }

    private async Task ApplyAsync()
    {
        if (!int.TryParse((_status?.Input ?? "").Trim(), out var status) || status <= 0)
        {
            _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]Enter a valid HTTP status code (e.g. 500).[/]" });
            return;
        }
        var newJson = HandlerPatch.Error((_msg?.Input ?? ""), status);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply error", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), $"error {_path}");
        if (result.Success) CloseWithResult(true);
        else _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{(result.Error ?? "").Replace("[", "[[").Replace("]", "]]")}[/]" });
    }
}
