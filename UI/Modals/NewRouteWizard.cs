// -----------------------------------------------------------------------
// LazyCaddy - guided "new reverse-proxy site" wizard. Collects a host and an
// upstream dial, previews the new route JSON via DiffConfirmDialog, then POSTs
// it to the server's routes list through the EditCoordinator (snapshot first).
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class NewRouteWizard : ModalBase<bool>
{
    private readonly EditCoordinator _editor;
    private readonly string _serverPath; // e.g. apps/http/servers/srv0
    private PromptControl? _host;
    private PromptControl? _upstream;
    private MarkupControl? _error;

    private NewRouteWizard(EditCoordinator editor, string serverPath) { _editor = editor; _serverPath = serverPath; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, EditCoordinator editor, string serverPath, Window? parent = null)
        => ((ModalBase<bool>)new NewRouteWizard(editor, serverPath)).ShowAsync(ws, parent);

    protected override string GetTitle() => " New reverse-proxy site ";
    protected override (int width, int height) GetSize() => (70, 14);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Create host → upstream reverse proxy.[/]").WithMargin(2, 1, 2, 0).Build());
        _host = Controls.Prompt("Host:     ").WithInputWidth(48).Build();
        _upstream = Controls.Prompt("Upstream: ").WithInputWidth(48).Build();
        Modal.AddControl(_host); Modal.AddControl(_upstream);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private async Task ApplyAsync()
    {
        var host = (_host?.Input ?? "").Trim();
        var up = (_upstream?.Input ?? "").Trim();
        if (host.Length == 0 || up.Length == 0) { Err("Host and upstream are required."); return; }

        var newJson = EditPatchBuilder.ReverseProxyRoute(host, up);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Add route", "(new)", newJson, Modal)) return;

        var result = await _editor.ApplyAsync(
            (admin, ct) => admin.PostConfigAsync($"{_serverPath}/routes", newJson, ct),
            $"add route {host} → {up}");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
