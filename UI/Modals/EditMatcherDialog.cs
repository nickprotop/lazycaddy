// -----------------------------------------------------------------------
// LazyCaddy - edit a route's host matcher (the match[] node).
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class EditMatcherDialog : ModalBase<bool>
{
    private readonly Route _route;
    private readonly EditCoordinator _editor;
    private PromptControl? _input;
    private MarkupControl? _error;

    private EditMatcherDialog(Route route, EditCoordinator editor) { _route = route; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new EditMatcherDialog(route, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Edit matcher — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (70, 12);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Comma-separated hostnames this route matches.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _input = Controls.Prompt("Hosts: ").WithInput(_route.HostOrMatch).WithInputWidth(50).Build();
        Modal.AddControl(_input);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build();
        Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel[/]")
            .WithMargin(2, 0, 2, 0).StickyBottom().Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; _ = ApplyAsync(); }
    }

    private async Task ApplyAsync()
    {
        var hosts = (_input?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (hosts.Length == 0) { Err("Enter at least one host."); return; }

        var path = $"{_route.ConfigPath}/match";
        string oldJson;
        try { oldJson = await _editor.GetConfigNodeAsync(path); }
        catch { oldJson = EditPatchBuilder.HostMatcher(_route.HostOrMatch.Split(", ", StringSplitOptions.RemoveEmptyEntries)); }
        var newJson = EditPatchBuilder.HostMatcher(hosts);

        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply matcher change", oldJson, newJson, Modal)) return;

        var result = await _editor.ApplyAsync(
            (admin, ct) => admin.PatchConfigAsync(path, newJson, ct),
            $"matcher {_route.HostOrMatch}: hosts=[{string.Join(", ", hosts)}]");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
