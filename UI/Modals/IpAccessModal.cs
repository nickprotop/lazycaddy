// -----------------------------------------------------------------------
// LazyCaddy - IpAccessModal: route-level IP allow/deny.
//
// Allow mode: merges an ip matcher (remote_ip / client_ip) into THIS route's
//   match[0] (without clobbering an existing host matcher) and PATCHes
//   {route.ConfigPath}/match.
// Deny mode: inserts a terminal 403 route BEFORE this route via PUT-at-index
//   into the server's routes[] array (Caddy PUT-at-array-index inserts).
// Both confirm via DiffConfirmDialog and write through EditCoordinator.
// Modeled on ForwardAuthModal's prompt scaffolding.
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class IpAccessModal : ModalBase<bool>
{
    private readonly Route _route;
    private readonly EditCoordinator _editor;

    private DropdownControl? _mode;
    private PromptControl? _cidrs;
    private CheckboxControl? _clientIp;
    private MarkupControl? _error;

    private static readonly string[] Modes = { "Allow only these", "Deny these" };

    private IpAccessModal(Route route, EditCoordinator editor)
    {
        _route = route; _editor = editor;
    }

    /// <summary>Show the IP access modal for a route. Returns true if a change was applied.</summary>
    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new IpAccessModal(route, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " IP access ";
    protected override (int width, int height) GetSize() => (72, 13);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Allow restricts this route to the listed CIDRs; Deny blocks them with a 403.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _mode = Controls.Dropdown("Mode:  ").AddItems(Modes).Build();
        Modal.AddControl(_mode);

        _cidrs = Controls.Prompt("CIDRs (comma-separated): ").WithInputWidth(34).Build();
        Modal.AddControl(_cidrs);

        _clientIp = new CheckboxControl { Label = "Behind a trusted proxy (use client_ip)", Checked = false };
        Modal.AddControl(_clientIp);

        _error = Controls.Markup().WithMargin(2, 0, 2, 0).Build();
        Modal.AddControl(_error);

        var ok = Controls.Button(" OK (Enter) ").Build(); ok.Click += (_, _) => Submit();
        var cancel = Controls.Button(" Cancel (Esc) ").Build(); cancel.Click += (_, _) => CloseWithResult(false);
        Modal.AddControl(Controls.HorizontalGrid().WithAlignment(HorizontalAlignment.Center).StickyBottom()
            .Column(c => c.Add(ok)).Column(c => c.Width(2)).Column(c => c.Add(cancel)).Build());
    }

    private void SetError(string msg)
        => _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{msg.Replace("[", "[[").Replace("]", "]]")}[/]" });

    private void Submit()
    {
        var ranges = (_cidrs?.Input ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        if (ranges.Length == 0) { SetError("Enter at least one CIDR."); return; }

        var clientIp = _clientIp?.Checked ?? false;
        var deny = (_mode?.SelectedIndex ?? 0) == 1;

        RunGuarded(() => deny ? ApplyDenyAsync(clientIp, ranges) : ApplyAllowAsync(clientIp, ranges),
            onError: SetError);
    }

    // Allow: merge the ip matcher's single key into THIS route's match[0] (creating one
    // if the match array is empty), preserving any existing host/path matcher there.
    private async Task ApplyAllowAsync(bool clientIp, string[] ranges)
    {
        string matchJson;
        try { matchJson = await _editor.GetConfigNodeAsync($"{_route.ConfigPath}/match"); }
        catch { matchJson = "[]"; }
        if (string.IsNullOrWhiteSpace(matchJson) || matchJson == "null") matchJson = "[]";

        JsonArray matchArr;
        try { matchArr = JsonNode.Parse(matchJson) as JsonArray ?? new JsonArray(); }
        catch { matchArr = new JsonArray(); }

        // The matcher object has exactly one key (remote_ip | client_ip) → copy it into match[0].
        var matcher = JsonNode.Parse(SecurityHandlerPatch.IpMatcher(clientIp, ranges))!.AsObject();

        JsonObject first;
        if (matchArr.Count == 0)
        {
            first = new JsonObject();
            matchArr.Add(first);
        }
        else
        {
            first = matchArr[0] as JsonObject ?? new JsonObject();
            matchArr[0] = first;
        }

        foreach (var kv in matcher)
            first[kv.Key] = kv.Value?.DeepClone();   // ADD the ip key; don't clobber existing matchers

        var newMatchJson = matchArr.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        var key = clientIp ? "client_ip" : "remote_ip";
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem,
                $"Allow only {string.Join(", ", ranges)}", matchJson, newMatchJson, Modal))
            return;

        var res = await _editor.ApplyAsync(
            (admin, ct) => admin.PatchConfigAsync($"{_route.ConfigPath}/match", newMatchJson, ct),
            $"allow {key} {string.Join(", ", ranges)} on {_route.HostOrMatch}");

        if (res.Success) CloseWithResult(true);
        else SetError(res.FriendlyError);
    }

    // Deny: insert a terminal 403 route BEFORE this route. routesPath is everything up to
    // and including "/routes"; N is the trailing integer of route.ConfigPath. PUT at
    // {routesPath}/{N} inserts at index N, shifting this route (and the rest) down.
    private async Task ApplyDenyAsync(bool clientIp, string[] ranges)
    {
        var (routesPath, n) = ParseRoutesPath(_route.ConfigPath);
        if (routesPath is null || n < 0) { SetError("Could not derive the route index from its config path."); return; }

        var denyJson = SecurityHandlerPatch.DenyRoute(clientIp, ranges);

        if (!await DiffConfirmDialog.ShowAsync(WindowSystem,
                $"Deny {string.Join(", ", ranges)}",
                $"(insert deny route before #{n})", denyJson, Modal))
            return;

        var key = clientIp ? "client_ip" : "remote_ip";
        var res = await _editor.ApplyAsync(
            (admin, ct) => admin.PutConfigAsync($"{routesPath}/{n}", denyJson, ct),
            $"deny {key} {string.Join(", ", ranges)} before {_route.HostOrMatch}");

        if (res.Success) CloseWithResult(true);
        else SetError(res.FriendlyError);
    }

    // "apps/http/servers/srv0/routes/3" → ("apps/http/servers/srv0/routes", 3).
    private static (string? routesPath, int index) ParseRoutesPath(string configPath)
    {
        if (string.IsNullOrEmpty(configPath)) return (null, -1);
        var slash = configPath.LastIndexOf('/');
        if (slash <= 0) return (null, -1);
        if (!int.TryParse(configPath[(slash + 1)..], out var n)) return (null, -1);
        return (configPath[..slash], n);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter) { Submit(); e.Handled = true; }
        else if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; }
    }
}
