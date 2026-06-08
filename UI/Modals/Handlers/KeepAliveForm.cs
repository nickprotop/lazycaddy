using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class KeepAliveForm : ModalBase<bool>
{
    private readonly string _path;   // = "{transportPath}/keep_alive"
    private readonly EditCoordinator _editor;
    private CheckboxControl? _setEnabled, _enabled;
    private PromptControl? _idle, _probe, _maxConns, _maxPerHost;
    private MarkupControl? _error;
    private string _original = "{}";

    private KeepAliveForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new KeepAliveForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Transport keep-alive ";
    protected override (int width, int height) GetSize() => (74, 15);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Connection pooling to upstreams. Durations like 2m, 30s. Check 'set enabled' to write enabled explicitly.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _setEnabled = new CheckboxControl { Label = "set 'enabled' explicitly", Checked = false };
        _enabled = new CheckboxControl { Label = "enabled (pooling on)", Checked = true };
        _idle = Controls.Prompt("Idle timeout:        ").WithInputWidth(20).Build();
        _probe = Controls.Prompt("Probe interval:      ").WithInputWidth(20).Build();
        _maxConns = Controls.Prompt("Max idle conns:      ").WithInput("0").WithInputWidth(12).Build();
        _maxPerHost = Controls.Prompt("Max idle/host:       ").WithInput("0").WithInputWidth(12).Build();
        Modal.AddControl(_setEnabled); Modal.AddControl(_enabled);
        Modal.AddControl(_idle); Modal.AddControl(_probe); Modal.AddControl(_maxConns); Modal.AddControl(_maxPerHost);
        _error = Controls.Markup().WithMargin(2, 0, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
        RunGuarded(LoadAsync, Err);
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return;
            if (r.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            { _setEnabled!.Checked = true; _enabled!.Checked = en.GetBoolean(); }
            Str(_idle, r, "idle_timeout"); Str(_probe, r, "probe_interval");
            Num(_maxConns, r, "max_idle_conns"); Num(_maxPerHost, r, "max_idle_conns_per_host");
        }
        catch (JsonException ex) { Err($"Could not parse keep_alive node: {ex.Message}"); }
        catch { }
    }

    private static void Str(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String) c.SetInput(v.GetString()); }
    private static void Num(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number) c.SetInput(v.ToString()); }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private async Task ApplyAsync()
    {
        int.TryParse((_maxConns?.Input ?? "0").Trim(), out var mc);
        int.TryParse((_maxPerHost?.Input ?? "0").Trim(), out var mph);
        var input = new KeepAliveInput(_setEnabled?.Checked ?? false, _enabled?.Checked ?? false,
            (_idle?.Input ?? "").Trim(), (_probe?.Input ?? "").Trim(), mc, mph);
        var newJson = HandlerPatch.KeepAlive(input);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply transport keep-alive", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), "transport keep_alive");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
