// -----------------------------------------------------------------------
// LazyCaddy - RewriteEditor: a rewrite handler node as a tab in the
// consolidated route modal. Ported from RewriteForm's load + patch-build.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class RewriteEditor : IConfigEditor
{
    private readonly string _path;
    private PromptControl? _method, _uri, _stripPre, _stripSuf;
    private Action? _onDirty;
    private string _original = "{}";

    private string _lMethod = "", _lUri = "", _lStripPre = "", _lStripSuf = "";

    public RewriteEditor(string path) => _path = path;

    public string TabTitle => "Rewrite";
    public string ConfigPath => _path;

    public void Build(IControlHost container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup().AddLine($"[{muted}]Internally rewrite the request.[/]").WithMargin(2, 1, 2, 0).Build());
        _method = Controls.Prompt("Method:       ").WithInputWidth(46).Build();
        _uri = Controls.Prompt("URI:          ").WithInputWidth(46).Build();
        _stripPre = Controls.Prompt("Strip prefix: ").WithInputWidth(46).Build();
        _stripSuf = Controls.Prompt("Strip suffix: ").WithInputWidth(46).Build();
        container.AddControl(_method); container.AddControl(_uri); container.AddControl(_stripPre); container.AddControl(_stripSuf);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            Set(_method, r, "method"); Set(_uri, r, "uri");
            Set(_stripPre, r, "strip_path_prefix"); Set(_stripSuf, r, "strip_path_suffix");
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private static void Set(PromptControl? c, JsonElement r, string key)
    {
        if (c is not null && r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) c.SetInput(v.GetString());
    }

    private void CaptureLoaded()
    {
        _lMethod = T(_method); _lUri = T(_uri); _lStripPre = T(_stripPre); _lStripSuf = T(_stripSuf);
    }

    public bool IsDirty =>
        T(_method) != _lMethod || T(_uri) != _lUri || T(_stripPre) != _lStripPre || T(_stripSuf) != _lStripSuf;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        var newJson = HandlerPatch.Rewrite(T(_method), T(_uri), T(_stripPre), T(_stripSuf));
        return new[] { new PendingWrite(_path, newJson, _original, "rewrite") };
    }

    public void Revert()
    {
        _method?.SetInput(_lMethod); _uri?.SetInput(_lUri); _stripPre?.SetInput(_lStripPre); _stripSuf?.SetInput(_lStripSuf);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
