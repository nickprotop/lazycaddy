// -----------------------------------------------------------------------
// LazyCaddy - RequestBodyEditor: a request_body handler node as a tab in the
// consolidated route modal. Ported from RequestBodyForm's load + patch-build.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class RequestBodyEditor : IConfigEditor
{
    private readonly string _path;
    private PromptControl? _maxSize;
    private Action? _onDirty;
    private string _original = "{}";

    private string _lMaxSize = "";

    public RequestBodyEditor(string path) => _path = path;

    public string TabTitle => "Request body";
    public string ConfigPath => _path;

    public void Build(IControlHost container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup().AddLine($"[{muted}]Limit the request body size (bytes; 0 = no limit).[/]").WithMargin(2, 1, 2, 0).Build());
        _maxSize = Controls.Prompt("Max size (bytes): ").WithInput("0").WithInputWidth(20).Build();
        container.AddControl(_maxSize);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original);
            if (d.RootElement.TryGetProperty("max_size", out var s)) _maxSize?.SetInput(s.ToString());
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded() => _lMaxSize = T(_maxSize);

    public bool IsDirty => T(_maxSize) != _lMaxSize;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        long.TryParse(T(_maxSize), out var max);
        var newJson = HandlerPatch.RequestBody(max);
        return new[] { new PendingWrite(_path, newJson, _original, "request_body") };
    }

    public void Revert() => _maxSize?.SetInput(_lMaxSize);

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
