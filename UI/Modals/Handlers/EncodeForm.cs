using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class EncodeForm : ModalBase<bool>
{
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private CheckboxControl? _gzip, _zstd;
    private PromptControl? _minLen;
    private MarkupControl? _error;
    private string _original = "{}";

    private EncodeForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new EncodeForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit encode ";
    protected override (int width, int height) GetSize() => (70, 12);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Response compression.[/]").WithMargin(2, 1, 2, 0).Build());
        _gzip = new CheckboxControl { Label = "gzip", Checked = true };
        _zstd = new CheckboxControl { Label = "zstd", Checked = false };
        _minLen = Controls.Prompt("Minimum length (bytes): ").WithInput("0").WithInputWidth(16).Build();
        Modal.AddControl(_gzip); Modal.AddControl(_zstd); Modal.AddControl(_minLen);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
        RunGuarded(LoadAsync, Err);
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.TryGetProperty("encodings", out var enc) && enc.ValueKind == JsonValueKind.Object)
            {
                if (_gzip is not null) _gzip.Checked = enc.TryGetProperty("gzip", out _);
                if (_zstd is not null) _zstd.Checked = enc.TryGetProperty("zstd", out _);
            }
            if (r.TryGetProperty("minimum_length", out var ml)) _minLen?.SetInput(ml.ToString());
        }
        catch (JsonException ex) { Err($"Could not parse encode node: {ex.Message}"); }
        catch { }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private async Task ApplyAsync()
    {
        int.TryParse((_minLen?.Input ?? "0").Trim(), out var min);
        var newJson = HandlerPatch.Encode(_gzip?.Checked ?? false, _zstd?.Checked ?? false, min);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply encode", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), $"encode {_path}");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
