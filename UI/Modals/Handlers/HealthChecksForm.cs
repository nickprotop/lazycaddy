using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class HealthChecksForm : ModalBase<bool>
{
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private PromptControl? _aUri, _aPort, _aMethod, _aInterval, _aTimeout, _aPasses, _aFails, _aStatus, _aBody;
    private PromptControl? _pFailDur, _pMaxFails, _pReqCount, _pStatus, _pLatency;
    private MarkupControl? _error;
    private string _original = "{}";

    private HealthChecksForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new HealthChecksForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Health checks ";
    protected override (int width, int height) GetSize() => (80, 26);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup(); var accent = UIConstants.Accent.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{accent}]Active (timer-based):[/]").WithMargin(2, 1, 2, 0).Build());
        _aUri = P("URI:        "); _aPort = P("Port:       "); _aMethod = P("Method:     ");
        _aInterval = P("Interval:   "); _aTimeout = P("Timeout:    ");
        _aPasses = P("Passes:     "); _aFails = P("Fails:      ");
        _aStatus = P("Exp status: "); _aBody = P("Exp body:   ");
        foreach (var c in new[] { _aUri, _aPort, _aMethod, _aInterval, _aTimeout, _aPasses, _aFails, _aStatus, _aBody }) Modal.AddControl(c);
        Modal.AddControl(Controls.Markup().AddLine($"[{accent}]Passive (request-observing):[/]").WithMargin(2, 0, 2, 0).Build());
        _pFailDur = P("Fail dur:   "); _pMaxFails = P("Max fails:  "); _pReqCount = P("Req count:  ");
        _pStatus = P("Bad status: "); _pLatency = P("Bad latency:");
        foreach (var c in new[] { _pFailDur, _pMaxFails, _pReqCount, _pStatus, _pLatency }) Modal.AddControl(c);
        _error = Controls.Markup().WithMargin(2, 0, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel   (durations like 10s, 250ms)[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
        RunGuarded(LoadAsync, Err);
    }

    private PromptControl P(string label) => Controls.Prompt(label).WithInputWidth(40).Build();

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync($"{_path}/health_checks");
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
        catch (JsonException ex) { Err($"Could not parse health_checks: {ex.Message}"); }
        catch { }
    }

    private static void S(PromptControl? c, JsonElement obj, string key)
    {
        if (c is not null && obj.TryGetProperty(key, out var v))
            c.SetInput(v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private async Task ApplyAsync()
    {
        int.TryParse(T(_aPort), out var port); int.TryParse(T(_aPasses), out var passes);
        int.TryParse(T(_aFails), out var fails); int.TryParse(T(_aStatus), out var status);
        var active = new ActiveHealthCheckInput(T(_aUri), port, T(_aMethod), T(_aInterval), T(_aTimeout),
            passes, fails, status, T(_aBody));
        int.TryParse(T(_pMaxFails), out var maxFails); int.TryParse(T(_pReqCount), out var reqCount);
        var badStatus = T(_pStatus).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToArray();
        var passive = new PassiveHealthCheckInput(T(_pFailDur), maxFails, reqCount, badStatus, T(_pLatency));
        var newJson = HandlerPatch.HealthChecks(active, passive);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply health_checks", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync($"{_path}/health_checks", newJson, ct), "health_checks");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
