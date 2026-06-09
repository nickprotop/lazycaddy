// -----------------------------------------------------------------------
// LazyCaddy - RateLimitEditor: the rate_limit handler node as a tab in the
// consolidated route modal. Models the first zone of `rate_limits` as a simple
// form (zone/key/window/max_events). Requires the caddy-ratelimit plugin.
// Modeled on HealthChecksEditor: load → form → BuildPatch → no host callbacks.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class RateLimitEditor : IConfigEditor
{
    private readonly string _path;          // the rate_limit handler node
    private PromptControl? _zone, _key, _window, _maxEvents;
    private Action? _onDirty;
    private string _origNodeJson = "{}";

    // Loaded-snapshot mirror for IsDirty + Revert.
    private string _lZone = "", _lKey = "", _lWindow = "", _lMax = "";

    public RateLimitEditor(string path) => _path = path;

    public string TabTitle => "Rate limit";
    public string ConfigPath => _path;

    public void Build(IControlHost container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Requires the caddy-ratelimit plugin (xcaddy build --with github.com/mholt/caddy-ratelimit).[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _zone      = Controls.Prompt("Zone:        ").WithInput("by_ip").WithInputWidth(40).Build();
        _key       = Controls.Prompt("Key:         ").WithInput("{http.request.remote.host}").WithInputWidth(40).Build();
        _window    = Controls.Prompt("Window:      ").WithInput("1m").WithInputWidth(12).Build();
        _maxEvents = Controls.Prompt("Max events:  ").WithInput("100").WithInputWidth(8).Build();
        foreach (var c in new[] { _zone, _key, _window, _maxEvents }) container.AddControl(c);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _origNodeJson = await editor.GetConfigNodeAsync(ConfigPath);
            using var d = JsonDocument.Parse(_origNodeJson); var r = d.RootElement;
            if (r.TryGetProperty("rate_limits", out var rl) && rl.ValueKind == JsonValueKind.Object)
            {
                foreach (var zone in rl.EnumerateObject())
                {
                    _zone?.SetInput(zone.Name);
                    var v = zone.Value;
                    if (v.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String) _key?.SetInput(k.GetString());
                    if (v.TryGetProperty("window", out var w) && w.ValueKind == JsonValueKind.String) _window?.SetInput(w.GetString());
                    if (v.TryGetProperty("max_events", out var m)) _maxEvents?.SetInput(m.ToString());
                    break; // first zone only
                }
            }
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded()
    {
        _lZone = T(_zone); _lKey = T(_key); _lWindow = T(_window); _lMax = T(_maxEvents);
    }

    public bool IsDirty =>
        T(_zone) != _lZone || T(_key) != _lKey || T(_window) != _lWindow || T(_maxEvents) != _lMax;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        if (!int.TryParse(T(_maxEvents), out var maxEvents)) maxEvents = 100;
        var newJson = SecurityHandlerPatch.RateLimit(T(_zone), T(_key), T(_window), maxEvents);
        return new[] { new PendingWrite(ConfigPath, newJson, _origNodeJson, "rate limit") };
    }

    public void Revert()
    {
        _zone?.SetInput(_lZone); _key?.SetInput(_lKey); _window?.SetInput(_lWindow); _maxEvents?.SetInput(_lMax);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
