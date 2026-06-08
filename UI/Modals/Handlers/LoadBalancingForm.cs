using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class LoadBalancingForm : ModalBase<bool>
{
    private readonly string _path;          // reverse_proxy handler node
    private readonly EditCoordinator _editor;
    private DropdownControl? _policy;
    private PromptControl? _param, _retries, _tryDur, _tryInt;
    private MarkupControl? _error;
    private string _original = "{}";

    private static readonly string[] Policies =
        { "(unchanged)", "round_robin", "least_conn", "random", "random_choose", "first",
          "ip_hash", "client_ip_hash", "uri_hash", "weighted_round_robin", "header", "cookie", "query" };

    private LoadBalancingForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new LoadBalancingForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Load balancing ";
    protected override (int width, int height) GetSize() => (76, 15);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Policy param: header=field · cookie=name · query=key · random_choose=N · weighted=w1,w2,…[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _policy = Controls.Dropdown("Policy:        ").AddItems(Policies).Build();
        _param  = Controls.Prompt("Policy param:  ").WithInputWidth(40).Build();
        _retries = Controls.Prompt("Retries:       ").WithInput("0").WithInputWidth(8).Build();
        _tryDur  = Controls.Prompt("Try duration:  ").WithInputWidth(12).Build();
        _tryInt  = Controls.Prompt("Try interval:  ").WithInputWidth(12).Build();
        Modal.AddControl(_policy); Modal.AddControl(_param);
        Modal.AddControl(_retries); Modal.AddControl(_tryDur); Modal.AddControl(_tryInt);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
        RunGuarded(LoadAsync, Err);
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync($"{_path}/load_balancing");
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.TryGetProperty("selection_policy", out var sp) && sp.TryGetProperty("policy", out var pn))
            {
                var name = pn.GetString();
                // Advanced LB (fallback or unknown policy) → leave "(unchanged)" and hint raw edit.
                if (sp.TryGetProperty("fallback", out _) || System.Array.IndexOf(Policies, name) < 1)
                {
                    Err("Advanced LB detected — use raw JSON (j) to edit fully.");
                }
                else
                {
                    var idx = System.Array.IndexOf(Policies, name);
                    if (idx > 0 && _policy is not null) _policy.SelectedIndex = idx;
                    // surface the single param if present
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
        catch (JsonException ex) { Err($"Could not parse load_balancing: {ex.Message}"); }
        catch { }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private async Task ApplyAsync()
    {
        var policy = _policy?.SelectedValue ?? "";
        if (policy == "(unchanged)") policy = "";  // don't touch selection_policy
        int.TryParse((_retries?.Input ?? "0").Trim(), out var retries);
        var newJson = HandlerPatch.LoadBalancing(policy, (_param?.Input ?? "").Trim(),
            retries, (_tryDur?.Input ?? "").Trim(), (_tryInt?.Input ?? "").Trim());
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply load_balancing", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync($"{_path}/load_balancing", newJson, ct),
            $"load_balancing {(_policy?.SelectedValue ?? "")}");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
