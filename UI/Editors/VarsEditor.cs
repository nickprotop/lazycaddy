// -----------------------------------------------------------------------
// LazyCaddy - VarsEditor: a vars handler node as a tab in the consolidated
// route modal. Ported from VarsForm's load + patch-build.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class VarsEditor : IConfigEditor
{
    private readonly string _path;
    private MultilineEditControl? _edit;
    private Action? _onDirty;
    private string _original = "{}";

    private string _lContent = "";

    public VarsEditor(string path) => _path = path;

    public string TabTitle => "Vars";
    public string ConfigPath => _path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup().AddLine($"[{muted}]One variable per line: name = value.[/]").WithMargin(2, 1, 2, 0).Build());
        _edit = Controls.MultilineEdit("").WithVerticalAlignment(VerticalAlignment.Fill).NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 1, 2, 0).Build();
        container.AddControl(_edit);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original);
            var lines = new List<string>();
            foreach (var p in d.RootElement.EnumerateObject())
                if (p.Name != "handler")
                    lines.Add($"{p.Name} = {(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())}");
            _edit?.SetContent(string.Join("\n", lines));
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded() => _lContent = Content();

    public bool IsDirty => Content() != _lContent;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        var entries = (_edit?.GetContent() ?? "").Split('\n')
            .Select(l => l.Split('=', 2))
            .Where(kv => kv.Length == 2 && kv[0].Trim().Length > 0)
            .Select(kv => (kv[0].Trim(), kv[1].Trim()))
            .ToArray();
        var newJson = HandlerPatch.Vars(entries);
        return new[] { new PendingWrite(_path, newJson, _original, "vars") };
    }

    public void Revert() => _edit?.SetContent(_lContent);

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private string Content() => (_edit?.GetContent() ?? "").Trim();
}
