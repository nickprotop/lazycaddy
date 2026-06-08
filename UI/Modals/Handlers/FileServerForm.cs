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
    private PromptControl? _root, _index, _hide, _precompressed, _statusCode;
    private CheckboxControl? _browse, _passThru, _canonicalSet, _canonical;
    private MarkupControl? _error;
    private string _original = "{}";
    private bool _hadBrowse;

    private FileServerForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new FileServerForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit file_server ";
    protected override (int width, int height) GetSize() => (78, 20);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Static file serving. Comma-separated lists. 'precompressed' module map via raw edit.[/]").WithMargin(2, 1, 2, 0).Build());
        _root = Controls.Prompt("Root:          ").WithInputWidth(46).Build();
        _index = Controls.Prompt("Index names:   ").WithInputWidth(46).Build();
        _hide = Controls.Prompt("Hide:          ").WithInputWidth(46).Build();
        _precompressed = Controls.Prompt("Precomp order: ").WithInputWidth(46).Build();
        _statusCode = Controls.Prompt("Status code:   ").WithInputWidth(46).Build();
        _browse = new CheckboxControl { Label = "Browse (directory listing) — press b to edit options", Checked = false };
        _passThru = new CheckboxControl { Label = "Pass through on 404", Checked = false };
        _canonicalSet = new CheckboxControl { Label = "set canonical_uris explicitly", Checked = false };
        _canonical = new CheckboxControl { Label = "canonical_uris (redirect to canonical path)", Checked = true };
        Modal.AddControl(_root); Modal.AddControl(_index); Modal.AddControl(_hide);
        Modal.AddControl(_precompressed); Modal.AddControl(_statusCode);
        Modal.AddControl(_browse); Modal.AddControl(_passThru);
        Modal.AddControl(_canonicalSet); Modal.AddControl(_canonical);
        _error = Controls.Markup().WithMargin(2, 0, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   b: browse options…   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
        RunGuarded(LoadAsync, Err);
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.TryGetProperty("root", out var root) && root.ValueKind == JsonValueKind.String) _root?.SetInput(root.GetString());
            _index?.SetInput(JoinArr(r, "index_names"));
            _hide?.SetInput(JoinArr(r, "hide"));
            _precompressed?.SetInput(JoinArr(r, "precompressed_order"));
            if (r.TryGetProperty("status_code", out var sc) && sc.ValueKind == JsonValueKind.String) _statusCode?.SetInput(sc.GetString());
            _hadBrowse = r.TryGetProperty("browse", out _);
            if (_browse is not null) _browse.Checked = _hadBrowse;
            if (_passThru is not null) _passThru.Checked = r.TryGetProperty("pass_thru", out var pt) && pt.ValueKind == JsonValueKind.True;
            if (r.TryGetProperty("canonical_uris", out var cu) && (cu.ValueKind == JsonValueKind.True || cu.ValueKind == JsonValueKind.False))
            { _canonicalSet!.Checked = true; _canonical!.Checked = cu.GetBoolean(); }
        }
        catch (JsonException ex) { Err($"Could not parse file_server node: {ex.Message}"); }
        catch { }
    }

    private static string JoinArr(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array
            ? string.Join(", ", a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()))
            : "";

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.B) { e.Handled = true; _ = BrowseForm.ShowAsync(WindowSystem, $"{_path}/browse", _editor, Modal); return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private static string[] Split(string? s) =>
        (s ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private async Task ApplyAsync()
    {
        var input = new FileServerInput(
            (_root?.Input ?? "").Trim(), Split(_index?.Input), Split(_hide?.Input),
            _passThru?.Checked ?? false, Split(_precompressed?.Input), (_statusCode?.Input ?? "").Trim(),
            _canonicalSet?.Checked ?? false, _canonical?.Checked ?? false);
        var managed = HandlerPatch.FileServer(input);
        // Preserve unmanaged keys (precompressed/fs/etag_file_extensions/browse) — Caddy PATCH replaces nodes.
        var newJson = HandlerPatch.MergeUnmanaged(_original, managed, HandlerPatch.ManagedFileServerKeys);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply file_server", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), $"file_server {_path}");
        if (!result.Success) { Err(result.Error ?? "Write failed."); return; }

        // Browse on/off as a targeted sub-node write so the merge above never erased its contents.
        var browseNow = _browse?.Checked ?? false;
        if (browseNow != _hadBrowse)
        {
            var r2 = browseNow
                ? await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync($"{_path}/browse", "{}", ct), "file_server browse on")
                : await _editor.ApplyAsync((a, ct) => a.DeleteConfigAsync($"{_path}/browse", ct), "file_server browse off");
            if (!r2.Success) { Err(r2.Error ?? "Browse toggle failed."); return; }
        }
        CloseWithResult(true);
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
