// -----------------------------------------------------------------------
// LazyCaddy - 3-way "unsaved changes" prompt shown when the route edit modal
// is dismissed (Esc) while one or more editor tabs are dirty. Lets the user
// Apply the pending changes, Discard them, or Cancel (stay in the modal).
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;

namespace LazyCaddy.UI.Modals;

public enum UnsavedChoice { Apply, Discard, Cancel }

public sealed class UnsavedChangesDialog : ModalBase<UnsavedChoice>
{
    private UnsavedChangesDialog() { }

    public static new Task<UnsavedChoice> ShowAsync(ConsoleWindowSystem ws, Window? parent = null)
        => ((ModalBase<UnsavedChoice>)new UnsavedChangesDialog()).ShowAsync(ws, parent);

    protected override string GetTitle() => " Unsaved changes ";
    protected override (int width, int height) GetSize() => (64, 9);
    protected override UnsavedChoice GetDefaultResult() => UnsavedChoice.Cancel;

    protected override void BuildContent()
    {
        Modal.AddControl(Controls.Markup()
            .AddLine("You have unsaved changes.")
            .AddEmptyLine()
            .AddLine($"[{UIConstants.MutedText.ToMarkup()}]Apply (a) saves them, Discard (d) drops them, Cancel (Esc) stays.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        var apply = Controls.Button(" Apply (a) ").Build(); apply.Click += (_, _) => CloseWithResult(UnsavedChoice.Apply);
        var discard = Controls.Button(" Discard (d) ").Build(); discard.Click += (_, _) => CloseWithResult(UnsavedChoice.Discard);
        var cancel = Controls.Button(" Cancel (Esc) ").Build(); cancel.Click += (_, _) => CloseWithResult(UnsavedChoice.Cancel);

        Modal.AddControl(Controls.HorizontalGrid().WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center).StickyBottom()
            .Column(c => c.Add(apply)).Column(c => c.Width(2))
            .Column(c => c.Add(discard)).Column(c => c.Width(2))
            .Column(c => c.Add(cancel)).Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        switch (e.KeyInfo.Key)
        {
            case ConsoleKey.A: CloseWithResult(UnsavedChoice.Apply); e.Handled = true; break;
            case ConsoleKey.D: CloseWithResult(UnsavedChoice.Discard); e.Handled = true; break;
            case ConsoleKey.C:
            case ConsoleKey.Escape: CloseWithResult(UnsavedChoice.Cancel); e.Handled = true; break;
        }
    }
}
