// -----------------------------------------------------------------------
// LazyCaddy - HttpTransportEditor: a reverse_proxy `transport` node (protocol
// "http") as a tab in the consolidated route modal. Ported from
// HttpTransportForm's load + patch-build (modal-wrapper dropped — the modal owns
// a single batched apply).
//
// Sibling tabs: HttpTransportForm had t/k sub-launchers for TlsConfigForm and
// KeepAliveForm. In the consolidated modal those are SEPARATE TABS
// (TlsConfigEditor / KeepAliveEditor), so this editor does NOT launch them and
// HandleKey returns false.
//
// Non-http guard: if the existing transport's protocol != "http", _nonHttp is
// set in LoadAsync (exactly as the form did) and BuildPatch returns no writes —
// applying here would force protocol "http" and clobber a non-http transport.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class HttpTransportEditor : IConfigEditor
{
    private readonly string _rpPath;     // reverse_proxy handler node path
    private string Path => $"{_rpPath}/transport";
    private CheckboxControl? _compression;
    private PromptControl? _maxConns, _dialTo, _dialFb, _respTo, _expectTo, _readTo, _writeTo,
        _maxHdr, _readBuf, _writeBuf, _versions, _localAddr, _proxyProto, _resolver;
    private MarkupControl? _nonHttpNote;
    private Action? _onDirty;
    private bool _nonHttp;
    private string _original = "{}";

    private bool _lCompression;
    private string _lMaxConns = "", _lDialTo = "", _lDialFb = "", _lRespTo = "", _lExpectTo = "",
        _lReadTo = "", _lWriteTo = "", _lMaxHdr = "", _lReadBuf = "", _lWriteBuf = "",
        _lVersions = "", _lLocalAddr = "", _lProxyProto = "", _lResolver = "";

    public HttpTransportEditor(string rpPath) => _rpPath = rpPath;

    public string TabTitle => "Transport";
    public string ConfigPath => Path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]HTTP transport to upstreams. Durations like 10s, 250ms. Versions/resolver comma-separated. TLS & keep-alive are sibling tabs.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _compression = new CheckboxControl { Label = "compression (enable)", Checked = false };
        _maxConns = P("Max conns/host:  "); _dialTo = P("Dial timeout:    "); _dialFb = P("Dial fallback:   ");
        _respTo = P("Resp hdr to:     "); _expectTo = P("Expect-cont to:  ");
        _readTo = P("Read timeout:    "); _writeTo = P("Write timeout:   ");
        _maxHdr = P("Max hdr bytes:   "); _readBuf = P("Read buf bytes:  "); _writeBuf = P("Write buf bytes: ");
        _versions = P("Versions:        "); _localAddr = P("Local address:   ");
        _proxyProto = P("Proxy protocol:  "); _resolver = P("Resolver addrs:  ");
        container.AddControl(_compression);
        foreach (var c in new[] { _maxConns, _dialTo, _dialFb, _respTo, _expectTo, _readTo, _writeTo,
                                  _maxHdr, _readBuf, _writeBuf, _versions, _localAddr, _proxyProto, _resolver })
            container.AddControl(c);
        _nonHttpNote = Controls.Markup().WithMargin(2, 0, 2, 0).Build();
        container.AddControl(_nonHttpNote);
    }

    private static PromptControl P(string label) => Controls.Prompt(label).WithInputWidth(24).Build();

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(Path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { CaptureLoaded(); _onDirty?.Invoke(); return; }
            if (r.TryGetProperty("protocol", out var proto) && proto.ValueKind == JsonValueKind.String
                && proto.GetString() is string ps && ps.Length > 0 && ps != "http")
            {
                _nonHttp = true;
                _nonHttpNote?.SetContent(new List<string> {
                    $"[{UIConstants.Bad.ToMarkup()}]transport protocol is '{ps.Replace("[", "[[").Replace("]", "]]")}' — use raw (j) to edit; this tab will not be applied.[/]" });
                CaptureLoaded(); _onDirty?.Invoke(); return;
            }
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
        catch (JsonException) { /* leave defaults */ }
        catch { /* node absent (404)/network → leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private static void Str(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String) c.SetInput(v.GetString()); }
    private static void Num(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number) c.SetInput(v.ToString()); }
    private static void Arr(PromptControl? c, JsonElement r, string k)
    { if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array)
          c.SetInput(string.Join(", ", v.EnumerateArray().Select(e => e.ToString()))); }

    private void CaptureLoaded()
    {
        _lCompression = _compression?.Checked ?? false;
        _lMaxConns = T(_maxConns); _lDialTo = T(_dialTo); _lDialFb = T(_dialFb); _lRespTo = T(_respTo);
        _lExpectTo = T(_expectTo); _lReadTo = T(_readTo); _lWriteTo = T(_writeTo); _lMaxHdr = T(_maxHdr);
        _lReadBuf = T(_readBuf); _lWriteBuf = T(_writeBuf); _lVersions = T(_versions); _lLocalAddr = T(_localAddr);
        _lProxyProto = T(_proxyProto); _lResolver = T(_resolver);
    }

    public bool IsDirty =>
        !_nonHttp && (
            (_compression?.Checked ?? false) != _lCompression ||
            T(_maxConns) != _lMaxConns || T(_dialTo) != _lDialTo || T(_dialFb) != _lDialFb || T(_respTo) != _lRespTo ||
            T(_expectTo) != _lExpectTo || T(_readTo) != _lReadTo || T(_writeTo) != _lWriteTo || T(_maxHdr) != _lMaxHdr ||
            T(_readBuf) != _lReadBuf || T(_writeBuf) != _lWriteBuf || T(_versions) != _lVersions ||
            T(_localAddr) != _lLocalAddr || T(_proxyProto) != _lProxyProto || T(_resolver) != _lResolver);

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        // Non-http transport: never clobber it by forcing protocol "http".
        if (_nonHttp) return Array.Empty<PendingWrite>();
        if (!IsDirty) return Array.Empty<PendingWrite>();
        var input = new HttpTransportInput(_compression?.Checked ?? false, N(_maxConns), T(_dialTo), T(_dialFb),
            T(_respTo), T(_expectTo), T(_readTo), T(_writeTo), N(_maxHdr), N(_readBuf), N(_writeBuf),
            Csv(_versions), T(_localAddr), T(_proxyProto), Csv(_resolver));
        var managedJson = HandlerPatch.HttpTransport(input);
        // Caddy PATCH replaces the whole node — merge managed fields over the current transport so
        // unmanaged sub-nodes (tls, keep_alive, network_proxy, …) survive a scalar edit here.
        var newJson = HandlerPatch.MergeTransport(_original, managedJson);
        return new[] { new PendingWrite(ConfigPath, newJson, _original, "transport") };
    }

    public void Revert()
    {
        if (_compression is not null) _compression.Checked = _lCompression;
        _maxConns?.SetInput(_lMaxConns); _dialTo?.SetInput(_lDialTo); _dialFb?.SetInput(_lDialFb); _respTo?.SetInput(_lRespTo);
        _expectTo?.SetInput(_lExpectTo); _readTo?.SetInput(_lReadTo); _writeTo?.SetInput(_lWriteTo); _maxHdr?.SetInput(_lMaxHdr);
        _readBuf?.SetInput(_lReadBuf); _writeBuf?.SetInput(_lWriteBuf); _versions?.SetInput(_lVersions); _localAddr?.SetInput(_lLocalAddr);
        _proxyProto?.SetInput(_lProxyProto); _resolver?.SetInput(_lResolver);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string[] Csv(PromptControl? c) =>
        (c?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static int N(PromptControl? c) { int.TryParse((c?.Input ?? "").Trim(), out var n); return n; }
    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
