// -----------------------------------------------------------------------
// LazyCaddy - MatchEditor: a route's host/path matcher as a tab in the
// consolidated route modal. Ported from EditRouteDialog's matcher load
// (parse the first matcher's host[]/path[]) + apply (EditPatchBuilder.
// HostPathMatcher) — modal-wrapper dropped, the modal owns a single batched
// apply.
//
// The ctor takes the route's ConfigPath; this editor's node is `{route}/match`.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class MatchEditor : IConfigEditor
{
    private readonly string _routePath;
    private string Path => $"{_routePath}/match";
    private PromptControl? _hosts, _paths;
    private Action? _onDirty;
    private string _original = "[]";

    private string _lHosts = "", _lPaths = "";

    public MatchEditor(string routePath) => _routePath = routePath;

    public string TabTitle => "Match";
    public string ConfigPath => Path;

    public void Build(IControlHost container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Host(s)/path(s) this route matches. Comma-separated.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _hosts = Controls.Prompt("Hosts: ").WithInputWidth(50).Build();
        _paths = Controls.Prompt("Paths: ").WithInputWidth(50).Build();
        container.AddControl(_hosts); container.AddControl(_paths);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(Path);
            using var doc = JsonDocument.Parse(_original);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];
                _hosts?.SetInput(string.Join(", ", StringArray(first, "host")));
                _paths?.SetInput(string.Join(", ", StringArray(first, "path")));
            }
        }
        catch (JsonException) { /* leave empty */ }
        catch { /* match absent (404)/network → leave empty */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private static string[] StringArray(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return arr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? "")
            .ToArray();
    }

    private void CaptureLoaded()
    {
        _lHosts = _hosts?.Input ?? ""; _lPaths = _paths?.Input ?? "";
    }

    public bool IsDirty =>
        (_hosts?.Input ?? "") != _lHosts || (_paths?.Input ?? "") != _lPaths;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        var hosts = Split(_hosts?.Input);
        var paths = Split(_paths?.Input);
        var newJson = EditPatchBuilder.HostPathMatcher(hosts, paths);
        return new[] { new PendingWrite(ConfigPath, newJson, _original, "match") };
    }

    public void Revert()
    {
        _hosts?.SetInput(_lHosts); _paths?.SetInput(_lPaths);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string[] Split(string? s) =>
        (s ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
