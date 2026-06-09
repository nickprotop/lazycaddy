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
    private readonly string _serverPath; // e.g. apps/http/servers/srv0
    private PromptControl? _host, _path;
    private DropdownControl? _type;
    private MarkupControl? _error;

    private NewRouteWizard(EditCoordinator editor, string serverPath) { _editor = editor; _serverPath = serverPath; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, EditCoordinator editor, string serverPath, Window? parent = null)
        => ((ModalBase<bool>)new NewRouteWizard(editor, serverPath)).ShowAsync(ws, parent);

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

        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Add route", "(new)", routeJson, Modal)) return;

        var hostLabel = hosts.Length > 0 ? string.Join(", ", hosts) : "(catch-all)";
        var result = await _editor.ApplyAsync(
            (admin, ct) => admin.PostConfigAsync($"{_serverPath}/routes", routeJson, ct),
            $"add route {hostLabel} [{chosen}]");
        if (!result.Success) { Err(result.Error ?? "Write failed."); return; }

        // Find the new route's index (it was appended) and open the matching handler form.
        int newIndex;
        try
        {
            var routesJson = await _editor.GetConfigNodeAsync($"{_serverPath}/routes");
            using var rd = JsonDocument.Parse(routesJson);
            newIndex = rd.RootElement.ValueKind == JsonValueKind.Array ? rd.RootElement.GetArrayLength() - 1 : 0;
        }
        catch { newIndex = 0; }

        // Open the new route's primary handler in the single-handler editor WHILE this wizard is
        // still open, parented to its live Modal — closing the wizard first would orphan the modal
        // on a closed window. The route already exists, so if the user cancels, the minimal-but-valid
        // route remains (it then appears in the grouped Routes view to expand + edit there).
        var routePath = $"{_serverPath}/routes/{newIndex}";
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
