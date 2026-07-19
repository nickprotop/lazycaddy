// -----------------------------------------------------------------------
// LazyCaddy - guided "new route" wizard: collect a host(+path) matcher and a
// handler type, create a minimal valid route, then open the single-handler editor
// on the new route's primary handler so the user fills in its details.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class NewRouteWizard : ModalBase<bool>
{
    private readonly EditCoordinator _editor;
    // The server the new route is written to. Seeded from the caller (the selected route's
    // server) and overridable via the picker below when the config has more than one server --
    // several servers can serve the same host on different ports, so "which one" is a real
    // question the user has to be able to answer.
    private readonly string _initialServerPath;
    private IReadOnlyList<ServerInfo> _servers = Array.Empty<ServerInfo>();
    private PromptControl? _host, _path;
    private DropdownControl? _type, _server;
    private MarkupControl? _error;

    // Resolved at apply time: the picker's choice, else the seed, else the only/first server.
    // The seed can be empty (no routes exist yet to infer a server from), so fall through.
    private string ServerPath =>
        _server is { SelectedIndex: >= 0 } dd && dd.SelectedIndex < _servers.Count
            ? _servers[dd.SelectedIndex].ConfigPath
            : !string.IsNullOrEmpty(_initialServerPath)
                ? _initialServerPath
                : _servers.Count > 0 ? _servers[0].ConfigPath : "";

    private NewRouteWizard(EditCoordinator editor, string serverPath, IReadOnlyList<ServerInfo> servers)
    {
        _editor = editor;
        _initialServerPath = serverPath;
        _servers = servers;
    }

    public static async Task<bool> ShowAsync(ConsoleWindowSystem ws, EditCoordinator editor, string serverPath, Window? parent = null)
    {
        // Read the server list up front so BuildContent can decide between a picker and a label.
        IReadOnlyList<ServerInfo> servers;
        try { servers = ConfigParser.ParseServers(await editor.GetRawConfigAsync()); }
        catch { servers = Array.Empty<ServerInfo>(); }
        return await ((ModalBase<bool>)new NewRouteWizard(editor, serverPath, servers)).ShowAsync(ws, parent);
    }

    protected override string GetTitle() => " New route ";
    protected override (int width, int height) GetSize() => (74, 15);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Host/path optional (empty = catch-all). Pick a handler type; its form opens next.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _host = Controls.Prompt("Host(s):  ").WithInputWidth(48).Build();
        _path = Controls.Prompt("Path(s):  ").WithInputWidth(48).Build();
        _type = Controls.Dropdown("Handler:  ")
            .AddItems(NewRouteSkeleton.OfferedTypes.Select(t => $"{t.Icon} {t.DisplayName}").ToArray()).Build();
        Modal.AddControl(_host); Modal.AddControl(_path); Modal.AddControl(_type);

        // Target server. With one server there is nothing to choose, so show it as plain text
        // rather than a one-item dropdown -- but still SHOW it, so the write target is never
        // invisible. With several, offer the picker seeded to the caller's server.
        if (_servers.Count > 1)
        {
            _server = Controls.Dropdown("Server:   ")
                .AddItems(_servers.Select(s => s.Label).ToArray()).Build();
            int seed = _servers.ToList().FindIndex(s => s.ConfigPath == _initialServerPath);
            _server.SelectedIndex = seed >= 0 ? seed : 0;
            Modal.AddControl(_server);
        }
        else if (_servers.Count == 1)
        {
            Modal.AddControl(Controls.Markup()
                .AddLine($"[{muted}]Server:   {_servers[0].Label}[/]")
                .WithMargin(2, 0, 2, 0).Build());
        }
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: create   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private static string[] Csv(string? s) =>
        (s ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private async Task ApplyAsync()
    {
        var idx = _type?.SelectedIndex ?? -1;
        if (idx < 0 || idx >= NewRouteSkeleton.OfferedTypes.Count) { Err("Pick a handler type."); return; }
        var chosen = NewRouteSkeleton.OfferedTypes[idx].Type;

        var hosts = Csv(_host?.Input);
        var paths = Csv(_path?.Input);
        var handler = NewRouteSkeleton.MinimalHandler(chosen);

        // Build the route: { [match], handle:[<minimal handler>] }. Omit match when no host/path.
        string routeJson;
        using (var hd = JsonDocument.Parse(handler))
        {
            var route = new Dictionary<string, object>();
            if (hosts.Length > 0 || paths.Length > 0)
            {
                using var md = JsonDocument.Parse(EditPatchBuilder.HostPathMatcher(hosts, paths));
                route["match"] = md.RootElement.Clone();
            }
            route["handle"] = new[] { hd.RootElement.Clone() };
            routeJson = JsonSerializer.Serialize(route, new JsonSerializerOptions { WriteIndented = true });
        }

        // Name the destination server in the confirm step: it's the last thing shown before the
        // write, and on a multi-server config it's the one detail the diff itself can't convey.
        var serverPath = ServerPath;
        var target = _servers.FirstOrDefault(s => s.ConfigPath == serverPath)?.Label ?? serverPath;
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, $"Add route → {target}", "(new)", routeJson, Modal)) return;

        var hostLabel = hosts.Length > 0 ? string.Join(", ", hosts) : "(catch-all)";
        var result = await _editor.ApplyAsync(
            RouteOp.Add($"{serverPath}/routes", routeJson, $"add route {hostLabel} [{chosen}]"),
            $"add route {hostLabel} [{chosen}]");
        if (!result.Success) { Err(result.Error ?? "Write failed."); return; }

        // Find the new route's index (it was appended) and open the matching handler form.
        int newIndex;
        try
        {
            var routesJson = await _editor.GetConfigNodeAsync($"{serverPath}/routes");
            using var rd = JsonDocument.Parse(routesJson);
            newIndex = rd.RootElement.ValueKind == JsonValueKind.Array ? rd.RootElement.GetArrayLength() - 1 : 0;
        }
        catch { newIndex = 0; }

        // Open the new route's primary handler in the single-handler editor WHILE this wizard is
        // still open, parented to its live Modal — closing the wizard first would orphan the modal
        // on a closed window. The route already exists, so if the user cancels, the minimal-but-valid
        // route remains (it then appears in the grouped Routes view to expand + edit there).
        var routePath = $"{serverPath}/routes/{newIndex}";
        string rawJson;
        try { rawJson = await _editor.GetConfigNodeAsync(routePath); }
        catch { rawJson = routeJson; } // fall back to the JSON we POSTed
        var newRoute = new Route(
            HostOrMatch: hostLabel,
            Upstream: "",
            TlsEnabled: false,
            Status: "",
            RawConfigJson: rawJson,
            ConfigPath: routePath);

        // Find the route's first real (non-subroute) handler and edit it directly.
        HandlerDescriptor? primary = null;
        try { primary = RouteModel.ParseHandlers(rawJson, routePath).FirstOrDefault(d => d.Type != "subroute"); }
        catch { /* leave null → just close; the route is in the list to edit */ }
        if (primary is not null)
            await SingleHandlerEditModal.ShowAsync(WindowSystem, newRoute, primary, _editor, Modal);
        CloseWithResult(true);
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
