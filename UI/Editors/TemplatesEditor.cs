// -----------------------------------------------------------------------
// LazyCaddy - TemplatesEditor: a templates handler node as a tab in the
// consolidated route modal. Ported from TemplatesForm's load + patch-build.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class TemplatesEditor : IConfigEditor
{
    private readonly string _path;
    private PromptControl? _fileRoot, _mime;
    private Action? _onDirty;
    private string _original = "{}";

    private string _lFileRoot = "", _lMime = "";

    public TemplatesEditor(string path) => _path = path;

    public string TabTitle => "Templates";
    public string ConfigPath => _path;

    public void Build(IControlHost container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup().AddLine($"[{muted}]Server-side templating.[/]").WithMargin(2, 1, 2, 0).Build());
        _fileRoot = Controls.Prompt("File root:  ").WithInputWidth(46).Build();
        _mime = Controls.Prompt("MIME types: ").WithInputWidth(46).Build();
        container.AddControl(_fileRoot); container.AddControl(_mime);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.TryGetProperty("file_root", out var fr) && fr.ValueKind == JsonValueKind.String) _fileRoot?.SetInput(fr.GetString());
            if (r.TryGetProperty("mime_types", out var mt) && mt.ValueKind == JsonValueKind.Array)
                _mime?.SetInput(string.Join(", ", mt.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString())));
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded()
    {
        _lFileRoot = T(_fileRoot); _lMime = T(_mime);
    }

    public bool IsDirty => T(_fileRoot) != _lFileRoot || T(_mime) != _lMime;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        var mime = (_mime?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var newJson = HandlerPatch.Templates(T(_fileRoot), mime);
        return new[] { new PendingWrite(_path, newJson, _original, "templates") };
    }

    public void Revert()
    {
        _fileRoot?.SetInput(_lFileRoot); _mime?.SetInput(_lMime);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
