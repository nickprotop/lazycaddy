// -----------------------------------------------------------------------
// LazyCaddy - EncodeEditor: an encode handler node as a tab in the consolidated
// route modal. Ported from EncodeForm's load + patch-build.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class EncodeEditor : IConfigEditor
{
    private readonly string _path;
    private CheckboxControl? _gzip, _zstd;
    private PromptControl? _minLen;
    private Action? _onDirty;
    private string _original = "{}";

    private bool _lGzip, _lZstd;
    private string _lMinLen = "";

    public EncodeEditor(string path) => _path = path;

    public string TabTitle => "Encode";
    public string ConfigPath => _path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup().AddLine($"[{muted}]Response compression.[/]").WithMargin(2, 1, 2, 0).Build());
        _gzip = new CheckboxControl { Label = "gzip", Checked = true };
        _zstd = new CheckboxControl { Label = "zstd", Checked = false };
        _minLen = Controls.Prompt("Minimum length (bytes): ").WithInput("0").WithInputWidth(16).Build();
        container.AddControl(_gzip); container.AddControl(_zstd); container.AddControl(_minLen);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.TryGetProperty("encodings", out var enc) && enc.ValueKind == JsonValueKind.Object)
            {
                if (_gzip is not null) _gzip.Checked = enc.TryGetProperty("gzip", out _);
                if (_zstd is not null) _zstd.Checked = enc.TryGetProperty("zstd", out _);
            }
            if (r.TryGetProperty("minimum_length", out var ml)) _minLen?.SetInput(ml.ToString());
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded()
    {
        _lGzip = _gzip?.Checked ?? false; _lZstd = _zstd?.Checked ?? false; _lMinLen = T(_minLen);
    }

    public bool IsDirty =>
        (_gzip?.Checked ?? false) != _lGzip || (_zstd?.Checked ?? false) != _lZstd || T(_minLen) != _lMinLen;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        int.TryParse(T(_minLen), out var min);
        var newJson = HandlerPatch.Encode(_gzip?.Checked ?? false, _zstd?.Checked ?? false, min);
        return new[] { new PendingWrite(_path, newJson, _original, "encode") };
    }

    public void Revert()
    {
        if (_gzip is not null) _gzip.Checked = _lGzip;
        if (_zstd is not null) _zstd.Checked = _lZstd;
        _minLen?.SetInput(_lMinLen);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
