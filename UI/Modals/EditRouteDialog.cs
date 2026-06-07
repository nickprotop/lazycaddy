// -----------------------------------------------------------------------
// LazyCaddy - edit a route's host matcher AND upstream(s) in one dialog.
// Replaces the separate EditUpstream/EditMatcher dialogs: a route has two
// editable things, so it's one "Edit route" form with both fields. Each field
// is applied via its own granular PATCH (only the changed ones), each gated by
// a diff/confirm and auto-snapshotted through EditCoordinator.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class EditRouteDialog : ModalBase<bool>
{
    private readonly Route _route;
    private readonly EditCoordinator _editor;
    private PromptControl? _hosts;
    private PromptControl? _upstreams;
    private MarkupControl? _error;
    private string? _resolvedUpstreamPath;

    private EditRouteDialog(Route route, EditCoordinator editor) { _route = route; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new EditRouteDialog(route, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Edit route — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (74, 14);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Host(s) this route matches and the upstream(s) it proxies to.[/]")
            .AddLine($"[{muted}]Comma-separated. Only changed fields are applied.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _hosts = Controls.Prompt("Hosts:     ").WithInput(_route.HostOrMatch).WithInputWidth(50).Build();
        _upstreams = Controls.Prompt("Upstreams: ").WithInput(_route.Upstream).WithInputWidth(50).Build();
        Modal.AddControl(_hosts);
        Modal.AddControl(_upstreams);

        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build();
        Modal.AddControl(_error);

        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Enter: review & apply   Esc: cancel[/]")
            .WithMargin(2, 0, 2, 0).StickyBottom().Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; _ = ApplyAsync(); }
    }

    private async Task ApplyAsync()
    {
        var hosts = Split(_hosts?.Input);
        var dials = Split(_upstreams?.Input);
        if (hosts.Length == 0) { ShowError("Enter at least one host."); return; }
        if (dials.Length == 0) { ShowError("Enter at least one upstream (host:port)."); return; }

        bool hostsChanged = !SameSet(hosts, _route.HostOrMatch);
        bool dialsChanged = !SameSet(dials, _route.Upstream);
        if (!hostsChanged && !dialsChanged) { CloseWithResult(false); return; }

        // Matcher first (host change), then upstream — each its own diff/confirm + PATCH.
        if (hostsChanged && !await ApplyMatcherAsync(hosts)) return;
        if (dialsChanged && !await ApplyUpstreamAsync(dials)) return;

        CloseWithResult(true);
    }

    private async Task<bool> ApplyMatcherAsync(string[] hosts)
    {
        var path = $"{_route.ConfigPath}/match";
        string oldJson;
        try { oldJson = await _editor.GetConfigNodeAsync(path); }
        catch { oldJson = EditPatchBuilder.HostMatcher(_route.HostOrMatch.Split(", ", StringSplitOptions.RemoveEmptyEntries)); }
        var newJson = EditPatchBuilder.HostMatcher(hosts);

        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply matcher change", oldJson, newJson, Modal))
            return false;

        var result = await _editor.ApplyAsync(
            (admin, ct) => admin.PatchConfigAsync(path, newJson, ct),
            $"matcher {_route.HostOrMatch}: hosts=[{string.Join(", ", hosts)}]");
        if (!result.Success) { ShowError(result.Error ?? "Matcher write failed."); return false; }
        return true;
    }

    private async Task<bool> ApplyUpstreamAsync(string[] dials)
    {
        string oldJson;
        try { oldJson = await ReadUpstreamsNodeAsync(); }
        catch { oldJson = EditPatchBuilder.UpstreamsArray(_route.Upstream.Split(", ", StringSplitOptions.RemoveEmptyEntries)); }
        var newJson = EditPatchBuilder.UpstreamsArray(dials);

        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply upstream change", oldJson, newJson, Modal))
            return false;

        var path = _resolvedUpstreamPath ?? $"{_route.ConfigPath}/handle/0/routes/0/handle/0/upstreams";
        var result = await _editor.ApplyAsync(
            (admin, ct) => admin.PatchConfigAsync(path, newJson, ct),
            $"upstream {_route.HostOrMatch} → {string.Join(", ", dials)}");
        if (!result.Success) { ShowError(result.Error ?? "Upstream write failed."); return false; }
        return true;
    }

    private async Task<string> ReadUpstreamsNodeAsync()
    {
        foreach (var candidate in new[]
        {
            $"{_route.ConfigPath}/handle/0/routes/0/handle/0/upstreams",
            $"{_route.ConfigPath}/handle/0/upstreams",
        })
        {
            try
            {
                var json = await _editor.GetConfigNodeAsync(candidate);
                if (!string.IsNullOrWhiteSpace(json)) { _resolvedUpstreamPath = candidate; return json; }
            }
            catch { /* try next */ }
        }
        throw new InvalidOperationException("upstreams node not found");
    }

    private static string[] Split(string? s) =>
        (s ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool SameSet(string[] entered, string current)
    {
        var cur = current.Split(", ", StringSplitOptions.RemoveEmptyEntries);
        return entered.Length == cur.Length && !entered.Except(cur).Any() && !cur.Except(entered).Any();
    }

    private void ShowError(string msg) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{msg.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
