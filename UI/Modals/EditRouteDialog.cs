// -----------------------------------------------------------------------
// LazyCaddy - edit a route's host/path matcher, upstream(s) and terminal flag
// in one dialog. Replaces the separate EditUpstream/EditMatcher dialogs: a
// route has a few editable things, so it's one "Edit route" form. Each part is
// applied via its own granular PATCH (only the changed ones), each gated by a
// diff/confirm and auto-snapshotted through EditCoordinator.
// -----------------------------------------------------------------------

using System.Text.Json;
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
    private PromptControl? _paths;
    private PromptControl? _upstreams;
    private CheckboxControl? _terminal;
    private MarkupControl? _error;
    private string? _resolvedUpstreamPath;

    // Original values read from the live config node (LoadCurrentAsync), used to
    // decide which parts actually changed at apply time.
    private string[] _origHosts = Array.Empty<string>();
    private string[] _origPaths = Array.Empty<string>();
    private bool _origTerminal;

    private EditRouteDialog(Route route, EditCoordinator editor) { _route = route; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new EditRouteDialog(route, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Edit route — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (74, 17);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Host(s)/path(s) this route matches and the upstream(s) it proxies to.[/]")
            .AddLine($"[{muted}]Comma-separated. Only changed fields are applied.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        // Pre-fill hosts/upstreams synchronously from the row model to avoid a blank
        // flash; LoadCurrentAsync then refines hosts/paths from the real match node.
        _hosts = Controls.Prompt("Hosts:     ").WithInput(_route.HostOrMatch).WithInputWidth(50).Build();
        _paths = Controls.Prompt("Paths:     ").WithInputWidth(50).Build();
        _upstreams = Controls.Prompt("Upstreams: ").WithInput(_route.Upstream).WithInputWidth(50).Build();
        _terminal = new CheckboxControl { Label = "Terminal (stop after match)", Checked = false };
        Modal.AddControl(_hosts);
        Modal.AddControl(_paths);
        Modal.AddControl(_upstreams);
        Modal.AddControl(_terminal);

        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build();
        Modal.AddControl(_error);

        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Enter: review & apply   Esc: cancel[/]")
            .WithMargin(2, 0, 2, 0).StickyBottom().Build());

        RunGuarded(LoadCurrentAsync, ShowError);
    }

    /// <summary>Read the live match + terminal nodes to pre-fill the form precisely
    /// (the row model's HostOrMatch may merge host+path into one string). A 404 on a
    /// field means it's absent — leave that control at its default.</summary>
    private async Task LoadCurrentAsync()
    {
        try
        {
            var matchJson = await _editor.GetConfigNodeAsync($"{_route.ConfigPath}/match");
            using var doc = JsonDocument.Parse(matchJson);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];
                _origHosts = StringArray(first, "host");
                _origPaths = StringArray(first, "path");
                _hosts?.SetInput(string.Join(", ", _origHosts));
                _paths?.SetInput(string.Join(", ", _origPaths));
            }
        }
        catch { /* match absent → leave host pre-fill, empty paths */ }

        try
        {
            var t = await _editor.GetConfigNodeAsync($"{_route.ConfigPath}/terminal");
            _origTerminal = t.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            if (_terminal is not null) _terminal.Checked = _origTerminal;
        }
        catch { _origTerminal = false; /* terminal absent → unchecked */ }
    }

    private static string[] StringArray(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return arr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? "")
            .ToArray();
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, ShowError); }
    }

    private async Task ApplyAsync()
    {
        var hosts = Split(_hosts?.Input);
        var paths = Split(_paths?.Input);
        var dials = Split(_upstreams?.Input);
        var terminal = _terminal?.Checked ?? false;
        if (hosts.Length == 0 && paths.Length == 0) { ShowError("Enter at least one host or path."); return; }
        if (dials.Length == 0) { ShowError("Enter at least one upstream (host:port)."); return; }

        bool matcherChanged = !SameSet(hosts, _origHosts) || !SameSet(paths, _origPaths);
        bool dialsChanged = !SameSet(dials, _route.Upstream.Split(", ", StringSplitOptions.RemoveEmptyEntries));
        bool terminalChanged = terminal != _origTerminal;
        if (!matcherChanged && !dialsChanged && !terminalChanged) { CloseWithResult(false); return; }

        // Matcher first, then upstream, then terminal — each its own diff/confirm +
        // PATCH, applied only if changed, stopping on first failure.
        if (matcherChanged && !await ApplyMatcherAsync(hosts, paths)) return;
        if (dialsChanged && !await ApplyUpstreamAsync(dials)) return;
        if (terminalChanged && !await ApplyTerminalAsync(terminal)) return;

        CloseWithResult(true);
    }

    private async Task<bool> ApplyMatcherAsync(string[] hosts, string[] paths)
    {
        var path = $"{_route.ConfigPath}/match";
        string oldJson;
        try { oldJson = await _editor.GetConfigNodeAsync(path); }
        catch { oldJson = EditPatchBuilder.HostPathMatcher(_origHosts, _origPaths); }
        var newJson = EditPatchBuilder.HostPathMatcher(hosts, paths);

        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply matcher change", oldJson, newJson, Modal))
            return false;

        var host0 = hosts.Length > 0 ? hosts[0] : (paths.Length > 0 ? paths[0] : _route.HostOrMatch);
        var result = await _editor.ApplyAsync(
            (admin, ct) => admin.UpsertConfigAsync(path, newJson, ct),
            $"matcher {host0}: hosts=[{string.Join(", ", hosts)}] paths=[{string.Join(", ", paths)}]");
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
            (admin, ct) => admin.UpsertConfigAsync(path, newJson, ct),
            $"upstream {_route.HostOrMatch} → {string.Join(", ", dials)}");
        if (!result.Success) { ShowError(result.Error ?? "Upstream write failed."); return false; }
        return true;
    }

    private async Task<bool> ApplyTerminalAsync(bool value)
    {
        var path = $"{_route.ConfigPath}/terminal";
        var oldJson = _origTerminal ? "true" : "false";
        var newJson = value ? "true" : "false";

        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply terminal change", oldJson, newJson, Modal))
            return false;

        var host0 = _route.HostOrMatch;
        var result = await _editor.ApplyAsync(
            (admin, ct) => admin.UpsertConfigAsync(path, newJson, ct),
            $"terminal {host0} → {newJson}");
        if (!result.Success) { ShowError(result.Error ?? "Terminal write failed."); return false; }
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
        => SameSet(entered, current.Split(", ", StringSplitOptions.RemoveEmptyEntries));

    private static bool SameSet(string[] entered, string[] cur)
        => entered.Length == cur.Length && !entered.Except(cur).Any() && !cur.Except(entered).Any();

    private void ShowError(string msg) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{msg.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
