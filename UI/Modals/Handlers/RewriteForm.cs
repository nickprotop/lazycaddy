using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class RewriteForm : ModalBase<bool>
{
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private PromptControl? _method, _uri, _stripPre, _stripSuf;
    private MarkupControl? _error;
    private string _original = "{}";

    private RewriteForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new RewriteForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit rewrite ";
    protected override (int width, int height) GetSize() => (74, 14);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Internally rewrite the request.[/]").WithMargin(2, 1, 2, 0).Build());
        _method = Controls.Prompt("Method:       ").WithInputWidth(46).Build();
        _uri = Controls.Prompt("URI:          ").WithInputWidth(46).Build();
        _stripPre = Controls.Prompt("Strip prefix: ").WithInputWidth(46).Build();
        _stripSuf = Controls.Prompt("Strip suffix: ").WithInputWidth(46).Build();
        Modal.AddControl(_method); Modal.AddControl(_uri); Modal.AddControl(_stripPre); Modal.AddControl(_stripSuf);
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
            Set(_method, r, "method"); Set(_uri, r, "uri");
            Set(_stripPre, r, "strip_path_prefix"); Set(_stripSuf, r, "strip_path_suffix");
        }
        catch (JsonException ex) { Err($"Could not parse rewrite node: {ex.Message}"); }
        catch { }
    }

    private static void Set(PromptControl? c, JsonElement r, string key)
    {
        if (c is not null && r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) c.SetInput(v.GetString());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; _ = ApplyAsync(); }
    }

    private async Task ApplyAsync()
    {
        var newJson = HandlerPatch.Rewrite((_method?.Input ?? "").Trim(), (_uri?.Input ?? "").Trim(),
            (_stripPre?.Input ?? "").Trim(), (_stripSuf?.Input ?? "").Trim());
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply rewrite", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), $"rewrite {_path}");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
