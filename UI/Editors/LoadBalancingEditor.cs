// -----------------------------------------------------------------------
// LazyCaddy - LoadBalancingEditor: the reverse_proxy load_balancing node as a
// tab in the consolidated route modal. Ported from LoadBalancingForm's load +
// patch-build, including its "(unchanged)" sentinel that leaves
// selection_policy untouched.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class LoadBalancingEditor : IConfigEditor
{
    private readonly string _path;          // reverse_proxy handler node; writes "{path}/load_balancing"
    private DropdownControl? _policy;
    private PromptControl? _param, _retries, _tryDur, _tryInt;
    private Action? _onDirty;
    private string _original = "{}";

    private static readonly string[] Policies =
        { "(unchanged)", "round_robin", "least_conn", "random", "random_choose", "first",
          "ip_hash", "client_ip_hash", "uri_hash", "weighted_round_robin", "header", "cookie", "query" };

    private string? _lPolicy;
    private string _lParam = "", _lRetries = "", _lTryDur = "", _lTryInt = "";

    public LoadBalancingEditor(string reverseProxyPath) => _path = reverseProxyPath;

    public string TabTitle => "Load balancing";
    public string ConfigPath => $"{_path}/load_balancing";

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Policy param: header=field · cookie=name · query=key · random_choose=N · weighted=w1,w2,…[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _policy = Controls.Dropdown("Policy:        ").AddItems(Policies).Build();
        _param  = Controls.Prompt("Policy param:  ").WithInputWidth(40).Build();
        _retries = Controls.Prompt("Retries:       ").WithInput("0").WithInputWidth(8).Build();
        _tryDur  = Controls.Prompt("Try duration:  ").WithInputWidth(12).Build();
        _tryInt  = Controls.Prompt("Try interval:  ").WithInputWidth(12).Build();
        container.AddControl(_policy); container.AddControl(_param);
        container.AddControl(_retries); container.AddControl(_tryDur); container.AddControl(_tryInt);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(ConfigPath);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.TryGetProperty("selection_policy", out var sp) && sp.TryGetProperty("policy", out var pn))
            {
                var name = pn.GetString();
                // Advanced LB (fallback or unknown policy) → leave "(unchanged)".
                if (!(sp.TryGetProperty("fallback", out _) || System.Array.IndexOf(Policies, name) < 1))
                {
                    var idx = System.Array.IndexOf(Policies, name);
                    if (idx > 0 && _policy is not null) _policy.SelectedIndex = idx;
                    foreach (var key in new[] { "field", "name", "key", "choose" })
                        if (sp.TryGetProperty(key, out var pv)) { _param?.SetInput(pv.ToString()); break; }
                    if (sp.TryGetProperty("weights", out var w) && w.ValueKind == JsonValueKind.Array)
                        _param?.SetInput(string.Join(",", w.EnumerateArray().Select(e => e.ToString())));
                }
            }
            if (r.TryGetProperty("retries", out var ret)) _retries?.SetInput(ret.ToString());
            if (r.TryGetProperty("try_duration", out var td) && td.ValueKind == JsonValueKind.String) _tryDur?.SetInput(td.GetString());
            if (r.TryGetProperty("try_interval", out var ti) && ti.ValueKind == JsonValueKind.String) _tryInt?.SetInput(ti.GetString());
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded()
    {
        _lPolicy = _policy?.SelectedValue;
        _lParam = T(_param); _lRetries = T(_retries); _lTryDur = T(_tryDur); _lTryInt = T(_tryInt);
    }

    public bool IsDirty =>
        (_policy?.SelectedValue) != _lPolicy ||
        T(_param) != _lParam || T(_retries) != _lRetries || T(_tryDur) != _lTryDur || T(_tryInt) != _lTryInt;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        var policy = _policy?.SelectedValue ?? "";
        if (policy == "(unchanged)") policy = "";  // don't touch selection_policy
        int.TryParse(T(_retries), out var retries);
        var newJson = HandlerPatch.LoadBalancing(policy, T(_param), retries, T(_tryDur), T(_tryInt));
        return new[] { new PendingWrite(ConfigPath, newJson, _original, $"load_balancing {(_policy?.SelectedValue ?? "")}") };
    }

    public void Revert()
    {
        if (_policy is not null) _policy.SelectedValue = _lPolicy;
        _param?.SetInput(_lParam); _retries?.SetInput(_lRetries); _tryDur?.SetInput(_lTryDur); _tryInt?.SetInput(_lTryInt);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
