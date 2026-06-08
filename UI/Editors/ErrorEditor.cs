// -----------------------------------------------------------------------
// LazyCaddy - ErrorEditor: an error handler node as a tab in the consolidated
// route modal. Ported from ErrorForm's load + patch-build.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class ErrorEditor : IConfigEditor
{
    private readonly string _path;
    private PromptControl? _msg, _status;
    private Action? _onDirty;
    private string _original = "{}";

    private string _lMsg = "", _lStatus = "";

    public ErrorEditor(string path) => _path = path;

    public string TabTitle => "Error";
    public string ConfigPath => _path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup().AddLine($"[{muted}]Return an error (triggers error routes).[/]").WithMargin(2, 1, 2, 0).Build());
        _msg = Controls.Prompt("Message:     ").WithInputWidth(48).Build();
        _status = Controls.Prompt("Status code: ").WithInput("500").WithInputWidth(10).Build();
        container.AddControl(_msg); container.AddControl(_status);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original);
            var r = d.RootElement;
            if (r.TryGetProperty("error", out var m) && m.ValueKind == JsonValueKind.String) _msg?.SetInput(m.GetString());
            if (r.TryGetProperty("status_code", out var s)) _status?.SetInput(s.ToString());
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* node absent (404)/network → leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded()
    {
        _lMsg = T(_msg); _lStatus = T(_status);
    }

    public bool IsDirty => T(_msg) != _lMsg || T(_status) != _lStatus;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        int.TryParse(T(_status), out var status);
        var newJson = HandlerPatch.Error((_msg?.Input ?? ""), status);
        return new[] { new PendingWrite(_path, newJson, _original, "error") };
    }

    public void Revert()
    {
        _msg?.SetInput(_lMsg); _status?.SetInput(_lStatus);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
