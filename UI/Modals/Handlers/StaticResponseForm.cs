using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class StaticResponseForm : ModalBase<bool>
{
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private PromptControl? _status, _body;
    private CheckboxControl? _close;
    private MarkupControl? _error;
    private string _original = "{}";

    private StaticResponseForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new StaticResponseForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit static_response ";
    protected override (int width, int height) GetSize() => (74, 13);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]A canned response.[/]").WithMargin(2, 1, 2, 0).Build());
        _status = Controls.Prompt("Status code: ").WithInput("200").WithInputWidth(10).Build();
        _body = Controls.Prompt("Body:        ").WithInputWidth(48).Build();
        _close = new CheckboxControl { Label = "Close connection after responding", Checked = false };
        Modal.AddControl(_status); Modal.AddControl(_body); Modal.AddControl(_close);
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
            if (r.TryGetProperty("status_code", out var s)) _status?.SetInput(s.ToString());
            if (r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String) _body?.SetInput(b.GetString());
            if (_close is not null) _close.Checked = r.TryGetProperty("close", out var c) && c.ValueKind == JsonValueKind.True;
        }
        catch (JsonException ex)
        {
            _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]Could not parse static_response node: {ex.Message.Replace("[", "[[").Replace("]", "]]")}[/]" });
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
            _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]Enter a valid HTTP status code (e.g. 200).[/]" });
            return;
        }
        var newJson = HandlerPatch.StaticResponse(status, (_body?.Input ?? ""), _close?.Checked ?? false);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply static_response", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.PatchConfigAsync(_path, newJson, ct), $"static_response {_path}");
        if (result.Success) CloseWithResult(true);
        else _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{(result.Error ?? "").Replace("[", "[[").Replace("]", "]]")}[/]" });
    }
}
