// -----------------------------------------------------------------------
// LazyCaddy - KeepAliveEditor: a reverse_proxy transport's `keep_alive` node as
// a tab in the consolidated route modal. Ported from KeepAliveForm's load +
// patch-build (modal-wrapper dropped — the modal owns a single batched apply).
//
// Tri-state `enabled`: a "set enabled explicitly" checkbox plus an "enabled"
// checkbox map to KeepAliveInput.EnabledSet/Enabled. Both are mirrored in _l*.
// No merge — keep_alive has no unmanaged keys, so the form patched the whole
// node; we do the same.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class KeepAliveEditor : IConfigEditor
{
    private readonly string _transportPath;   // reverse_proxy transport node path
    private string Path => $"{_transportPath}/keep_alive";
    private CheckboxControl? _setEnabled, _enabled;
    private PromptControl? _idle, _probe, _maxConns, _maxPerHost;
    private Action? _onDirty;
    private string _original = "{}";

    private bool _lSetEnabled, _lEnabled;
    private string _lIdle = "", _lProbe = "", _lMaxConns = "", _lMaxPerHost = "";

    public KeepAliveEditor(string transportPath) => _transportPath = transportPath;

    public string TabTitle => "Keep-alive";
    public string ConfigPath => Path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Connection pooling to upstreams. Durations like 2m, 30s. Check 'set enabled' to write enabled explicitly.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _setEnabled = new CheckboxControl { Label = "set 'enabled' explicitly", Checked = false };
        _enabled = new CheckboxControl { Label = "enabled (pooling on)", Checked = true };
        _idle = Controls.Prompt("Idle timeout:        ").WithInputWidth(20).Build();
        _probe = Controls.Prompt("Probe interval:      ").WithInputWidth(20).Build();
        _maxConns = Controls.Prompt("Max idle conns:      ").WithInput("0").WithInputWidth(12).Build();
        _maxPerHost = Controls.Prompt("Max idle/host:       ").WithInput("0").WithInputWidth(12).Build();
        container.AddControl(_setEnabled); container.AddControl(_enabled);
        container.AddControl(_idle); container.AddControl(_probe);
        container.AddControl(_maxConns); container.AddControl(_maxPerHost);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(Path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.ValueKind == JsonValueKind.Object)
            {
                if (r.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
                { _setEnabled!.Checked = true; _enabled!.Checked = en.GetBoolean(); }
                Str(_idle, r, "idle_timeout"); Str(_probe, r, "probe_interval");
                Num(_maxConns, r, "max_idle_conns"); Num(_maxPerHost, r, "max_idle_conns_per_host");
            }
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* node absent (404)/network → leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private static void Str(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String) c.SetInput(v.GetString()); }
    private static void Num(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number) c.SetInput(v.ToString()); }

    private void CaptureLoaded()
    {
        _lSetEnabled = _setEnabled?.Checked ?? false;
        _lEnabled = _enabled?.Checked ?? false;
        _lIdle = T(_idle); _lProbe = T(_probe); _lMaxConns = T(_maxConns); _lMaxPerHost = T(_maxPerHost);
    }

    public bool IsDirty =>
        (_setEnabled?.Checked ?? false) != _lSetEnabled ||
        (_enabled?.Checked ?? false) != _lEnabled ||
        T(_idle) != _lIdle || T(_probe) != _lProbe || T(_maxConns) != _lMaxConns || T(_maxPerHost) != _lMaxPerHost;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        int.TryParse(T(_maxConns), out var mc);
        int.TryParse(T(_maxPerHost), out var mph);
        var input = new KeepAliveInput(_setEnabled?.Checked ?? false, _enabled?.Checked ?? false,
            T(_idle), T(_probe), mc, mph);
        // No merge — keep_alive has no unmanaged keys (the form patched the whole node).
        var newJson = HandlerPatch.KeepAlive(input);
        return new[] { new PendingWrite(ConfigPath, newJson, _original, "transport keep_alive") };
    }

    public void Revert()
    {
        if (_setEnabled is not null) _setEnabled.Checked = _lSetEnabled;
        if (_enabled is not null) _enabled.Checked = _lEnabled;
        _idle?.SetInput(_lIdle); _probe?.SetInput(_lProbe);
        _maxConns?.SetInput(_lMaxConns); _maxPerHost?.SetInput(_lMaxPerHost);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
