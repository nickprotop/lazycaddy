// -----------------------------------------------------------------------
// LazyCaddy - edit a route's reverse_proxy upstream dial list.
// Reads the upstreams node, lets the user edit a comma-separated dial list,
// then goes through DiffConfirmDialog + EditCoordinator to apply.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class EditUpstreamDialog : ModalBase<bool>
{
    private readonly Route _route;
    private readonly EditCoordinator _editor;
    private PromptControl? _input;
    private MarkupControl? _error;
    private string? _resolvedPath;

    private EditUpstreamDialog(Route route, EditCoordinator editor) { _route = route; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new EditUpstreamDialog(route, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Edit upstream — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (70, 12);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Comma-separated dial addresses (host:port). Current shown below.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _input = Controls.Prompt("Upstreams: ").WithInput(_route.Upstream).WithInputWidth(50).Build();
        Modal.AddControl(_input);

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
        var dials = (_input?.Input ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (dials.Length == 0) { ShowError("Enter at least one host:port."); return; }

        string oldJson;
        try { oldJson = await ReadUpstreamsNodeAsync(); }
        catch { oldJson = EditPatchBuilder.UpstreamsArray(_route.Upstream.Split(", ", StringSplitOptions.RemoveEmptyEntries)); }

        var newJson = EditPatchBuilder.UpstreamsArray(dials);
        var ok = await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply upstream change", oldJson, newJson, Modal);
        if (!ok) return;

        // _resolvedPath set by ReadUpstreamsNodeAsync if it succeeded; else fall back to the
        // common nested subroute path.
        var path = _resolvedPath ?? $"{_route.ConfigPath}/handle/0/routes/0/handle/0/upstreams";
        var result = await _editor.ApplyAsync(
            (admin, ct) => admin.PatchConfigAsync(path, newJson, ct),
            snapshotLabel: $"upstream {_route.HostOrMatch} → {string.Join(", ", dials)}");

        if (result.Success) CloseWithResult(true);
        else ShowError(result.Error ?? "Write failed.");
    }

    // Try likely config paths to find this route's upstreams node (handles subroute nesting).
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
                if (!string.IsNullOrWhiteSpace(json)) { _resolvedPath = candidate; return json; }
            }
            catch { /* try next */ }
        }
        throw new InvalidOperationException("upstreams node not found");
    }

    private void ShowError(string msg) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{msg.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
