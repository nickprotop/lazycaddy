// -----------------------------------------------------------------------
// LazyCaddy - StaticResponseEditor: a static_response handler node as a tab in
// the consolidated route modal. Ported from StaticResponseForm's load +
// patch-build (modal-wrapper dropped — the modal owns a single batched apply).
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class StaticResponseEditor : IConfigEditor
{
    private readonly string _path;
    private PromptControl? _status, _body;
    private CheckboxControl? _close;
    private Action? _onDirty;
    private string _original = "{}";

    private string _lStatus = "", _lBody = "";
    private bool _lClose;

    public StaticResponseEditor(string path) => _path = path;

    public string TabTitle => "Static response";
    public string ConfigPath => _path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup().AddLine($"[{muted}]A canned response.[/]").WithMargin(2, 1, 2, 0).Build());
        _status = Controls.Prompt("Status code: ").WithInput("200").WithInputWidth(10).Build();
        _body = Controls.Prompt("Body:        ").WithInputWidth(48).Build();
        _close = new CheckboxControl { Label = "Close connection after responding", Checked = false };
        container.AddControl(_status); container.AddControl(_body); container.AddControl(_close);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original);
            var r = d.RootElement;
            if (r.TryGetProperty("status_code", out var s)) _status?.SetInput(s.ToString());
            if (r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String) _body?.SetInput(b.GetString());
            if (_close is not null) _close.Checked = r.TryGetProperty("close", out var c) && c.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* node absent (404)/network → leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded()
    {
        _lStatus = T(_status); _lBody = T(_body); _lClose = _close?.Checked ?? false;
    }

    public bool IsDirty =>
        T(_status) != _lStatus || T(_body) != _lBody || (_close?.Checked ?? false) != _lClose;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        int.TryParse(T(_status), out var status);
        var newJson = HandlerPatch.StaticResponse(status, (_body?.Input ?? ""), _close?.Checked ?? false);
        return new[] { new PendingWrite(_path, newJson, _original, "static_response") };
    }

    public void Revert()
    {
        _status?.SetInput(_lStatus); _body?.SetInput(_lBody);
        if (_close is not null) _close.Checked = _lClose;
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
