// -----------------------------------------------------------------------
// LazyCaddy - TlsConfigEditor: a reverse_proxy transport's `tls` sub-node as a
// tab in the consolidated route modal. Ported from TlsConfigForm's load +
// patch-build (modal-wrapper dropped — the modal owns a single batched apply).
// `ca` (polymorphic) stays unmanaged and is preserved by the merge.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class TlsConfigEditor : IConfigEditor
{
    private readonly string _path;   // = "{transportPath}/tls"
    private CheckboxControl? _insecure;
    private PromptControl? _serverName, _reneg, _handshake, _curves, _exceptPorts;
    private Action? _onDirty;
    private string _original = "{}";

    private bool _lInsecure;
    private string _lServerName = "", _lReneg = "", _lHandshake = "", _lCurves = "", _lExceptPorts = "";

    public TlsConfigEditor(string path) => _path = path;

    public string TabTitle => "TLS";
    public string ConfigPath => _path;

    public void Build(IControlHost container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]TLS to the upstream. renegotiation: never/once/freely. Curves/ports comma-separated. 'ca' via raw edit.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _insecure = new CheckboxControl { Label = "insecure_skip_verify (skip cert validation)", Checked = false };
        _serverName = Controls.Prompt("Server name:  ").WithInputWidth(44).Build();
        _reneg = Controls.Prompt("Renegotiation:").WithInputWidth(44).Build();
        _handshake = Controls.Prompt("Handshake to: ").WithInputWidth(44).Build();
        _curves = Controls.Prompt("Curves:       ").WithInputWidth(44).Build();
        _exceptPorts = Controls.Prompt("Except ports: ").WithInputWidth(44).Build();
        container.AddControl(_insecure); container.AddControl(_serverName); container.AddControl(_reneg);
        container.AddControl(_handshake); container.AddControl(_curves); container.AddControl(_exceptPorts);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.ValueKind == JsonValueKind.Object)
            {
                if (r.TryGetProperty("insecure_skip_verify", out var iv) && iv.ValueKind == JsonValueKind.True) _insecure!.Checked = true;
                Str(_serverName, r, "server_name"); Str(_reneg, r, "renegotiation"); Str(_handshake, r, "handshake_timeout");
                Arr(_curves, r, "curves"); Arr(_exceptPorts, r, "except_ports");
            }
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* node absent (404)/network → leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private static void Str(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String) c.SetInput(v.GetString()); }
    private static void Arr(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array)
          c.SetInput(string.Join(", ", v.EnumerateArray().Select(e => e.ToString()))); }

    private void CaptureLoaded()
    {
        _lInsecure = _insecure?.Checked ?? false;
        _lServerName = T(_serverName); _lReneg = T(_reneg); _lHandshake = T(_handshake);
        _lCurves = T(_curves); _lExceptPorts = T(_exceptPorts);
    }

    public bool IsDirty =>
        (_insecure?.Checked ?? false) != _lInsecure ||
        T(_serverName) != _lServerName || T(_reneg) != _lReneg || T(_handshake) != _lHandshake ||
        T(_curves) != _lCurves || T(_exceptPorts) != _lExceptPorts;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        var input = new TlsConfigInput(_insecure?.Checked ?? false, (_serverName?.Input ?? "").Trim(),
            (_reneg?.Input ?? "").Trim(), (_handshake?.Input ?? "").Trim(), Csv(_curves), Csv(_exceptPorts));
        var managedJson = HandlerPatch.TlsConfig(input);
        var merged = HandlerPatch.MergeTlsConfig(_original, managedJson);
        return new[] { new PendingWrite(ConfigPath, merged, _original, "transport tls") };
    }

    public void Revert()
    {
        if (_insecure is not null) _insecure.Checked = _lInsecure;
        _serverName?.SetInput(_lServerName); _reneg?.SetInput(_lReneg); _handshake?.SetInput(_lHandshake);
        _curves?.SetInput(_lCurves); _exceptPorts?.SetInput(_lExceptPorts);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string[] Csv(PromptControl? c) =>
        (c?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
