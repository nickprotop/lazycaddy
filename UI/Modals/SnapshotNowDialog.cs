// -----------------------------------------------------------------------
// LazyCaddy - manual snapshot prompt: asks for an optional description,
// then the caller captures the current running config under that label.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;

namespace LazyCaddy.UI.Modals;

/// <summary>Prompts for a manual-snapshot description. Returns null on cancel.</summary>
public sealed class SnapshotNowDialog : ModalBase<string?>
{
    private PromptControl? _input;

    private SnapshotNowDialog() { }

    public static new Task<string?> ShowAsync(ConsoleWindowSystem ws, Window? parent = null)
        => ((ModalBase<string?>)new SnapshotNowDialog()).ShowAsync(ws, parent);

    protected override string GetTitle() => " Snapshot now ";
    protected override (int width, int height) GetSize() => (66, 9);
    protected override string? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Describe this snapshot (optional).[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _input = Controls.Prompt("Description: ").WithInputWidth(46).Build();
        Modal.AddControl(_input);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: save   Esc: cancel[/]")
            .WithMargin(2, 1, 2, 0).StickyBottom().Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(null); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            var label = (_input?.Input ?? "").Trim();
            CloseWithResult(label.Length == 0 ? "manual snapshot" : label);
            e.Handled = true;
        }
    }
}
