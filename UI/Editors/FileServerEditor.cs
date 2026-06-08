// -----------------------------------------------------------------------
// LazyCaddy - FileServerEditor: a file_server handler node as a tab in the
// consolidated route modal. Ported from FileServerForm's load + patch-build
// (modal-wrapper dropped — the modal owns a single batched apply).
//
// Browse note: FileServerForm had a "Browse (directory listing)" checkbox that
// did a SEPARATE targeted write to {path}/browse (upsert "{}" on, delete off).
// In the consolidated modal, browse contents are their OWN tab (BrowseEditor),
// so this editor deliberately DROPS the browse checkbox. Browse stays unmanaged
// in ManagedFileServerKeys, so the MergeUnmanaged below preserves any existing
// `browse` node untouched; enabling browse = editing the Browse tab and applying
// (which upserts {fs}/browse). The "turn browse off" affordance is lost here —
// noted for CM-5 (acceptable for now).
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class FileServerEditor : IConfigEditor
{
    private readonly string _path;
    private PromptControl? _root, _index, _hide, _precompressed, _statusCode;
    private CheckboxControl? _passThru, _canonicalSet, _canonical;
    private Action? _onDirty;
    private string _original = "{}";

    private string _lRoot = "", _lIndex = "", _lHide = "", _lPrecompressed = "", _lStatusCode = "";
    private bool _lPassThru, _lCanonicalSet, _lCanonical;

    public FileServerEditor(string path) => _path = path;

    public string TabTitle => "File server";
    public string ConfigPath => _path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup().AddLine($"[{muted}]Static file serving. Comma-separated lists. 'precompressed' module map via raw edit.[/]").WithMargin(2, 1, 2, 0).Build());
        _root = Controls.Prompt("Root:          ").WithInputWidth(46).Build();
        _index = Controls.Prompt("Index names:   ").WithInputWidth(46).Build();
        _hide = Controls.Prompt("Hide:          ").WithInputWidth(46).Build();
        _precompressed = Controls.Prompt("Precomp order: ").WithInputWidth(46).Build();
        _statusCode = Controls.Prompt("Status code:   ").WithInputWidth(46).Build();
        _passThru = new CheckboxControl { Label = "Pass through on 404", Checked = false };
        _canonicalSet = new CheckboxControl { Label = "set canonical_uris explicitly", Checked = false };
        _canonical = new CheckboxControl { Label = "canonical_uris (redirect to canonical path)", Checked = true };
        container.AddControl(_root); container.AddControl(_index); container.AddControl(_hide);
        container.AddControl(_precompressed); container.AddControl(_statusCode);
        container.AddControl(_passThru); container.AddControl(_canonicalSet); container.AddControl(_canonical);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.TryGetProperty("root", out var root) && root.ValueKind == JsonValueKind.String) _root?.SetInput(root.GetString());
            _index?.SetInput(JoinArr(r, "index_names"));
            _hide?.SetInput(JoinArr(r, "hide"));
            _precompressed?.SetInput(JoinArr(r, "precompressed_order"));
            if (r.TryGetProperty("status_code", out var sc) && sc.ValueKind == JsonValueKind.String) _statusCode?.SetInput(sc.GetString());
            if (_passThru is not null) _passThru.Checked = r.TryGetProperty("pass_thru", out var pt) && pt.ValueKind == JsonValueKind.True;
            if (r.TryGetProperty("canonical_uris", out var cu) && (cu.ValueKind == JsonValueKind.True || cu.ValueKind == JsonValueKind.False))
            { _canonicalSet!.Checked = true; _canonical!.Checked = cu.GetBoolean(); }
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* node absent (404)/network → leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private static string JoinArr(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array
            ? string.Join(", ", a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()))
            : "";

    private void CaptureLoaded()
    {
        _lRoot = T(_root); _lIndex = T(_index); _lHide = T(_hide);
        _lPrecompressed = T(_precompressed); _lStatusCode = T(_statusCode);
        _lPassThru = _passThru?.Checked ?? false;
        _lCanonicalSet = _canonicalSet?.Checked ?? false;
        _lCanonical = _canonical?.Checked ?? false;
    }

    public bool IsDirty =>
        T(_root) != _lRoot || T(_index) != _lIndex || T(_hide) != _lHide ||
        T(_precompressed) != _lPrecompressed || T(_statusCode) != _lStatusCode ||
        (_passThru?.Checked ?? false) != _lPassThru ||
        (_canonicalSet?.Checked ?? false) != _lCanonicalSet ||
        (_canonical?.Checked ?? false) != _lCanonical;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        var input = new FileServerInput(
            (_root?.Input ?? "").Trim(), Split(_index?.Input), Split(_hide?.Input),
            _passThru?.Checked ?? false, Split(_precompressed?.Input), (_statusCode?.Input ?? "").Trim(),
            _canonicalSet?.Checked ?? false, _canonical?.Checked ?? false);
        var managed = HandlerPatch.FileServer(input);
        // Preserve unmanaged keys (precompressed/fs/etag_file_extensions/browse) — Caddy PATCH replaces nodes.
        var newJson = HandlerPatch.MergeUnmanaged(_original, managed, HandlerPatch.ManagedFileServerKeys);
        return new[] { new PendingWrite(ConfigPath, newJson, _original, "file_server") };
    }

    public void Revert()
    {
        _root?.SetInput(_lRoot); _index?.SetInput(_lIndex); _hide?.SetInput(_lHide);
        _precompressed?.SetInput(_lPrecompressed); _statusCode?.SetInput(_lStatusCode);
        if (_passThru is not null) _passThru.Checked = _lPassThru;
        if (_canonicalSet is not null) _canonicalSet.Checked = _lCanonicalSet;
        if (_canonical is not null) _canonical.Checked = _lCanonical;
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string[] Split(string? s) =>
        (s ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
