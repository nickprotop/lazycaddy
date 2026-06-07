// -----------------------------------------------------------------------
// LazyCaddy - generic "are you sure?" delete confirmation. A snapshot is
// always captured before the actual delete, so this is reversible.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;

namespace LazyCaddy.UI.Modals;

public sealed class ConfirmDeleteDialog : ModalBase<bool>
{
    private readonly string _what;
    private ConfirmDeleteDialog(string what) => _what = what;

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string what, Window? parent = null)
        => ((ModalBase<bool>)new ConfirmDeleteDialog(what)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Confirm delete ";
    protected override (int width, int height) GetSize() => (60, 9);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        Modal.AddControl(Controls.Markup()
            .AddLine($"Delete [{UIConstants.Bad.ToMarkup()}]{_what.Replace("[", "[[").Replace("]", "]]")}[/]?")
            .AddEmptyLine()
            .AddLine($"[{UIConstants.MutedText.ToMarkup()}]A snapshot is taken first; this is reversible.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        var yes = Controls.Button(" Delete (y) ").Build(); yes.Click += (_, _) => CloseWithResult(true);
        var no = Controls.Button(" Cancel (Esc) ").Build(); no.Click += (_, _) => CloseWithResult(false);
        Modal.AddControl(Controls.HorizontalGrid().WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center).StickyBottom()
            .Column(c => c.Add(yes)).Column(c => c.Width(2)).Column(c => c.Add(no)).Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key is ConsoleKey.Y) { CloseWithResult(true); e.Handled = true; }
        else if (e.KeyInfo.Key is ConsoleKey.Escape or ConsoleKey.N) { CloseWithResult(false); e.Handled = true; }
    }
}
