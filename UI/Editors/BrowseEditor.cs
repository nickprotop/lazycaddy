// -----------------------------------------------------------------------
// LazyCaddy - BrowseEditor: a file_server `browse` sub-node (directory-listing
// options) as a tab in the consolidated route modal. Ported from BrowseForm's
// load + patch-build.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class BrowseEditor : IConfigEditor
{
    private readonly string _path;   // = "{fileServerPath}/browse" (matches BrowseForm's call site)
    private PromptControl? _template, _sort, _fileLimit;
    private CheckboxControl? _revealSymlinks;
    private Action? _onDirty;
    private string _original = "{}";

    private string _lTemplate = "", _lSort = "", _lFileLimit = "";
    private bool _lRevealSymlinks;

    // BrowseForm is shown with "{fileServerPath}/browse"; mirror that here.
    public BrowseEditor(string fileServerPath) => _path = $"{fileServerPath}/browse";

    public string TabTitle => "Browse";
    public string ConfigPath => _path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Directory listing options. Sort: comma list e.g. 'name,asc' or 'time,desc'.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _template = Controls.Prompt("Template file: ").WithInputWidth(44).Build();
        _sort = Controls.Prompt("Sort:          ").WithInputWidth(44).Build();
        _fileLimit = Controls.Prompt("File limit:    ").WithInput("0").WithInputWidth(12).Build();
        _revealSymlinks = new CheckboxControl { Label = "reveal_symlinks (show symlink targets)", Checked = false };
        container.AddControl(_template); container.AddControl(_sort); container.AddControl(_fileLimit); container.AddControl(_revealSymlinks);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.ValueKind == JsonValueKind.Object)
            {
                if (r.TryGetProperty("template_file", out var tf) && tf.ValueKind == JsonValueKind.String) _template?.SetInput(tf.GetString());
                if (r.TryGetProperty("reveal_symlinks", out var rs) && rs.ValueKind == JsonValueKind.True) _revealSymlinks!.Checked = true;
                if (r.TryGetProperty("sort", out var so) && so.ValueKind == JsonValueKind.Array)
                    _sort?.SetInput(string.Join(", ", so.EnumerateArray().Select(e => e.ToString())));
                if (r.TryGetProperty("file_limit", out var fl) && fl.ValueKind == JsonValueKind.Number) _fileLimit?.SetInput(fl.ToString());
            }
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded()
    {
        _lTemplate = T(_template); _lSort = T(_sort); _lFileLimit = T(_fileLimit);
        _lRevealSymlinks = _revealSymlinks?.Checked ?? false;
    }

    public bool IsDirty =>
        T(_template) != _lTemplate || T(_sort) != _lSort || T(_fileLimit) != _lFileLimit ||
        (_revealSymlinks?.Checked ?? false) != _lRevealSymlinks;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        int.TryParse(T(_fileLimit), out var fileLimit);
        var sort = (_sort?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var input = new BrowseInput(T(_template), _revealSymlinks?.Checked ?? false, sort, fileLimit);
        var newJson = HandlerPatch.Browse(input);
        return new[] { new PendingWrite(_path, newJson, _original, "file_server browse") };
    }

    public void Revert()
    {
        _template?.SetInput(_lTemplate); _sort?.SetInput(_lSort); _fileLimit?.SetInput(_lFileLimit);
        if (_revealSymlinks is not null) _revealSymlinks.Checked = _lRevealSymlinks;
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
