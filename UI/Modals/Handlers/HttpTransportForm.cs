using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

// Edits a reverse_proxy `transport` (protocol "http"). Nested tls/keep_alive via t/k keys.
public sealed class HttpTransportForm : ModalBase<bool>
{
    private readonly string _rpPath;     // reverse_proxy handler node path
    private readonly EditCoordinator _editor;
    private string _path => $"{_rpPath}/transport";
    private CheckboxControl? _compression;
    private PromptControl? _maxConns, _dialTo, _dialFb, _respTo, _expectTo, _readTo, _writeTo,
        _maxHdr, _readBuf, _writeBuf, _versions, _localAddr, _proxyProto, _resolver;
    private MarkupControl? _error;
    private bool _nonHttp;
    private string _original = "{}";

    private HttpTransportForm(string rpPath, EditCoordinator editor) { _rpPath = rpPath; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string rpPath, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new HttpTransportForm(rpPath, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " HTTP transport ";
    protected override (int width, int height) GetSize() => (84, 26);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]HTTP transport to upstreams. Durations like 10s, 250ms. Versions/resolver comma-separated.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _compression = new CheckboxControl { Label = "compression (enable)", Checked = false };
        _maxConns = P("Max conns/host:  "); _dialTo = P("Dial timeout:    "); _dialFb = P("Dial fallback:   ");
        _respTo = P("Resp hdr to:     "); _expectTo = P("Expect-cont to:  ");
        _readTo = P("Read timeout:    "); _writeTo = P("Write timeout:   ");
        _maxHdr = P("Max hdr bytes:   "); _readBuf = P("Read buf bytes:  "); _writeBuf = P("Write buf bytes: ");
        _versions = P("Versions:        "); _localAddr = P("Local address:   ");
        _proxyProto = P("Proxy protocol:  "); _resolver = P("Resolver addrs:  ");
        Modal.AddControl(_compression);
        foreach (var c in new[] { _maxConns, _dialTo, _dialFb, _respTo, _expectTo, _readTo, _writeTo,
                                  _maxHdr, _readBuf, _writeBuf, _versions, _localAddr, _proxyProto, _resolver })
            Modal.AddControl(c);
        _error = Controls.Markup().WithMargin(2, 0, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Enter: apply   t: TLS…   k: keep-alive…   Esc: cancel[/]")
            .WithMargin(2, 0, 2, 0).StickyBottom().Build());
        RunGuarded(LoadAsync, Err);
    }

    private PromptControl P(string label) => Controls.Prompt(label).WithInputWidth(24).Build();

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return;
            if (r.TryGetProperty("protocol", out var proto) && proto.ValueKind == JsonValueKind.String
                && proto.GetString() is string ps && ps.Length > 0 && ps != "http")
            { _nonHttp = true; Err($"transport protocol is '{ps}' — use raw (j) to edit; applying here would force http."); return; }
            if (r.TryGetProperty("compression", out var cp) && cp.ValueKind == JsonValueKind.True) _compression!.Checked = true;
            Num(_maxConns, r, "max_conns_per_host");
            Str(_dialTo, r, "dial_timeout"); Str(_dialFb, r, "dial_fallback_delay");
            Str(_respTo, r, "response_header_timeout"); Str(_expectTo, r, "expect_continue_timeout");
            Str(_readTo, r, "read_timeout"); Str(_writeTo, r, "write_timeout");
            Num(_maxHdr, r, "max_response_header_size"); Num(_readBuf, r, "read_buffer_size"); Num(_writeBuf, r, "write_buffer_size");
            Arr(_versions, r, "versions"); Str(_localAddr, r, "local_address"); Str(_proxyProto, r, "proxy_protocol");
            if (r.TryGetProperty("resolver", out var rs) && rs.TryGetProperty("addresses", out var ad) && ad.ValueKind == JsonValueKind.Array)
                _resolver?.SetInput(string.Join(", ", ad.EnumerateArray().Select(e => e.ToString())));
        }
        catch (JsonException ex) { Err($"Could not parse transport node: {ex.Message}"); }
        catch { }
    }

    private static void Str(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String) c.SetInput(v.GetString()); }
    private static void Num(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number) c.SetInput(v.ToString()); }
    private static void Arr(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array)
          c.SetInput(string.Join(", ", v.EnumerateArray().Select(e => e.ToString()))); }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.T) { e.Handled = true; _ = TlsConfigForm.ShowAsync(WindowSystem, $"{_path}/tls", _editor, Modal); return; }
        if (e.KeyInfo.Key == ConsoleKey.K) { e.Handled = true; _ = KeepAliveForm.ShowAsync(WindowSystem, $"{_path}/keep_alive", _editor, Modal); return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private static string[] Csv(PromptControl? c) =>
        (c?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static int N(PromptControl? c) { int.TryParse((c?.Input ?? "").Trim(), out var n); return n; }
    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();

    private async Task ApplyAsync()
    {
        if (_nonHttp) { Err("Non-http transport — edit via raw (j)."); return; }
        var input = new HttpTransportInput(_compression?.Checked ?? false, N(_maxConns), T(_dialTo), T(_dialFb),
            T(_respTo), T(_expectTo), T(_readTo), T(_writeTo), N(_maxHdr), N(_readBuf), N(_writeBuf),
            Csv(_versions), T(_localAddr), T(_proxyProto), Csv(_resolver));
        var newJson = HandlerPatch.HttpTransport(input);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply HTTP transport", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), "transport");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
