// -----------------------------------------------------------------------
// LazyCaddy - HealthChecksEditor: the reverse_proxy health_checks node as a
// tab in the consolidated route modal. Ported from HealthChecksForm's load +
// patch-build, with the modal-wrapper (CloseWithResult/DiffConfirm/self-apply)
// dropped — the modal owns a single batched apply.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class HealthChecksEditor : IConfigEditor
{
    private readonly string _reverseProxyPath;
    private PromptControl? _aUri, _aPort, _aMethod, _aInterval, _aTimeout, _aPasses, _aFails, _aStatus, _aBody;
    private PromptControl? _pFailDur, _pMaxFails, _pReqCount, _pStatus, _pLatency;
    private Action? _onDirty;
    private string _original = "{}";

    // Loaded-snapshot mirror of every control's value, captured after LoadAsync,
    // for IsDirty comparison and Revert.
    private string _lUri = "", _lPort = "", _lMethod = "", _lInterval = "", _lTimeout = "",
        _lPasses = "", _lFails = "", _lStatus = "", _lBody = "";
    private string _lFailDur = "", _lMaxFails = "", _lReqCount = "", _lPStatus = "", _lLatency = "";

    public HealthChecksEditor(string reverseProxyPath) => _reverseProxyPath = reverseProxyPath;

    public string TabTitle => "Health checks";
    public string ConfigPath => _reverseProxyPath;

    public void Build(IControlHost container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var accent = UIConstants.Accent.ToMarkup();

        container.AddControl(Controls.Markup().AddLine($"[{accent}]Active (timer-based):[/]").WithMargin(2, 1, 2, 0).Build());
        _aUri = P("URI:        "); _aPort = P("Port:       "); _aMethod = P("Method:     ");
        _aInterval = P("Interval:   "); _aTimeout = P("Timeout:    ");
        _aPasses = P("Passes:     "); _aFails = P("Fails:      ");
        _aStatus = P("Exp status: "); _aBody = P("Exp body:   ");
        foreach (var c in new[] { _aUri, _aPort, _aMethod, _aInterval, _aTimeout, _aPasses, _aFails, _aStatus, _aBody })
            container.AddControl(c);

        container.AddControl(Controls.Markup().AddLine($"[{accent}]Passive (request-observing):[/]").WithMargin(2, 0, 2, 0).Build());
        _pFailDur = P("Fail dur:   "); _pMaxFails = P("Max fails:  "); _pReqCount = P("Req count:  ");
        _pStatus = P("Bad status: "); _pLatency = P("Bad latency:");
        foreach (var c in new[] { _pFailDur, _pMaxFails, _pReqCount, _pStatus, _pLatency })
            container.AddControl(c);
    }

    private static PromptControl P(string label) => Controls.Prompt(label).WithInputWidth(40).Build();

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync($"{ConfigPath}/health_checks");
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.TryGetProperty("active", out var a))
            {
                S(_aUri, a, "uri"); S(_aPort, a, "port"); S(_aMethod, a, "method");
                S(_aInterval, a, "interval"); S(_aTimeout, a, "timeout");
                S(_aPasses, a, "passes"); S(_aFails, a, "fails"); S(_aStatus, a, "expect_status"); S(_aBody, a, "expect_body");
            }
            if (r.TryGetProperty("passive", out var p))
            {
                S(_pFailDur, p, "fail_duration"); S(_pMaxFails, p, "max_fails"); S(_pReqCount, p, "unhealthy_request_count");
                if (p.TryGetProperty("unhealthy_status", out var us) && us.ValueKind == JsonValueKind.Array)
                    _pStatus?.SetInput(string.Join(",", us.EnumerateArray().Select(e => e.ToString())));
                S(_pLatency, p, "unhealthy_latency");
            }
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private static void S(PromptControl? c, JsonElement obj, string key)
    {
        if (c is not null && obj.TryGetProperty(key, out var v))
            c.SetInput(v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString());
    }

    // Snapshot the loaded control values so we can detect edits + revert.
    private void CaptureLoaded()
    {
        _lUri = T(_aUri); _lPort = T(_aPort); _lMethod = T(_aMethod); _lInterval = T(_aInterval);
        _lTimeout = T(_aTimeout); _lPasses = T(_aPasses); _lFails = T(_aFails); _lStatus = T(_aStatus); _lBody = T(_aBody);
        _lFailDur = T(_pFailDur); _lMaxFails = T(_pMaxFails); _lReqCount = T(_pReqCount);
        _lPStatus = T(_pStatus); _lLatency = T(_pLatency);
    }

    public bool IsDirty =>
        T(_aUri) != _lUri || T(_aPort) != _lPort || T(_aMethod) != _lMethod || T(_aInterval) != _lInterval ||
        T(_aTimeout) != _lTimeout || T(_aPasses) != _lPasses || T(_aFails) != _lFails || T(_aStatus) != _lStatus ||
        T(_aBody) != _lBody || T(_pFailDur) != _lFailDur || T(_pMaxFails) != _lMaxFails || T(_pReqCount) != _lReqCount ||
        T(_pStatus) != _lPStatus || T(_pLatency) != _lLatency;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();

        int.TryParse(T(_aPort), out var port); int.TryParse(T(_aPasses), out var passes);
        int.TryParse(T(_aFails), out var fails); int.TryParse(T(_aStatus), out var status);
        var active = new ActiveHealthCheckInput(T(_aUri), port, T(_aMethod), T(_aInterval), T(_aTimeout),
            passes, fails, status, T(_aBody));
        int.TryParse(T(_pMaxFails), out var maxFails); int.TryParse(T(_pReqCount), out var reqCount);
        var badStatus = T(_pStatus).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToArray();
        var passive = new PassiveHealthCheckInput(T(_pFailDur), maxFails, reqCount, badStatus, T(_pLatency));
        var newJson = HandlerPatch.HealthChecks(active, passive);

        return new[] { new PendingWrite($"{ConfigPath}/health_checks", newJson, _original, "health_checks") };
    }

    public void Revert()
    {
        _aUri?.SetInput(_lUri); _aPort?.SetInput(_lPort); _aMethod?.SetInput(_lMethod); _aInterval?.SetInput(_lInterval);
        _aTimeout?.SetInput(_lTimeout); _aPasses?.SetInput(_lPasses); _aFails?.SetInput(_lFails);
        _aStatus?.SetInput(_lStatus); _aBody?.SetInput(_lBody);
        _pFailDur?.SetInput(_lFailDur); _pMaxFails?.SetInput(_lMaxFails); _pReqCount?.SetInput(_lReqCount);
        _pStatus?.SetInput(_lPStatus); _pLatency?.SetInput(_lLatency);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
}
