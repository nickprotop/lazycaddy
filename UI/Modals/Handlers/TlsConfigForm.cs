using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

// Edits a reverse_proxy transport's `tls` sub-node. `ca` (polymorphic) stays raw-edit.
public sealed class TlsConfigForm : ModalBase<bool>
{
    private readonly string _path;   // = "{transportPath}/tls"
    private readonly EditCoordinator _editor;
    private CheckboxControl? _insecure;
    private PromptControl? _serverName, _reneg, _handshake, _curves, _exceptPorts;
    private MarkupControl? _error;
    private string _original = "{}";

    private TlsConfigForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new TlsConfigForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Transport TLS ";
    protected override (int width, int height) GetSize() => (76, 16);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]TLS to the upstream. renegotiation: never/once/freely. Curves/ports comma-separated. 'ca' via raw edit.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _insecure = new CheckboxControl { Label = "insecure_skip_verify (skip cert validation)", Checked = false };
        _serverName = Controls.Prompt("Server name:  ").WithInputWidth(44).Build();
        _reneg = Controls.Prompt("Renegotiation:").WithInputWidth(44).Build();
        _handshake = Controls.Prompt("Handshake to: ").WithInputWidth(44).Build();
        _curves = Controls.Prompt("Curves:       ").WithInputWidth(44).Build();
        _exceptPorts = Controls.Prompt("Except ports: ").WithInputWidth(44).Build();
        Modal.AddControl(_insecure); Modal.AddControl(_serverName); Modal.AddControl(_reneg);
        Modal.AddControl(_handshake); Modal.AddControl(_curves); Modal.AddControl(_exceptPorts);
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
            if (r.TryGetProperty("insecure_skip_verify", out var iv) && iv.ValueKind == JsonValueKind.True) _insecure!.Checked = true;
            Str(_serverName, r, "server_name"); Str(_reneg, r, "renegotiation"); Str(_handshake, r, "handshake_timeout");
            Arr(_curves, r, "curves"); Arr(_exceptPorts, r, "except_ports");
        }
        catch (JsonException ex) { Err($"Could not parse tls node: {ex.Message}"); }
        catch { }
    }

    private static void Str(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String) c.SetInput(v.GetString()); }
    private static void Arr(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array)
          c.SetInput(string.Join(", ", v.EnumerateArray().Select(e => e.ToString()))); }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private static string[] Csv(PromptControl? c) =>
        (c?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private async Task ApplyAsync()
    {
        var input = new TlsConfigInput(_insecure?.Checked ?? false, (_serverName?.Input ?? "").Trim(),
            (_reneg?.Input ?? "").Trim(), (_handshake?.Input ?? "").Trim(), Csv(_curves), Csv(_exceptPorts));
        var managedJson = HandlerPatch.TlsConfig(input);
        var merged = HandlerPatch.MergeTlsConfig(_original, managedJson);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply transport TLS", _original, merged, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, merged, ct), "transport tls");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
