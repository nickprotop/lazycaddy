// -----------------------------------------------------------------------
// LazyCaddy - shared confirm dialog: shows old vs new JSON and asks to apply.
// Returns true to apply, false to cancel.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class DiffConfirmDialog : ModalBase<bool>
{
    private readonly string _title;
    private readonly string _oldJson;
    private readonly string _newJson;

    private DiffConfirmDialog(string title, string oldJson, string newJson)
    {
        _title = title; _oldJson = oldJson; _newJson = newJson;
    }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string title, string oldJson, string newJson, Window? parent = null)
        => ((ModalBase<bool>)new DiffConfirmDialog(title, oldJson, newJson)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" {_title} ";
    protected override (int width, int height) GetSize() => (96, 32);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Review the change. Current (left) vs new (right). Apply writes to the live Caddy.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        var current = Controls.MultilineEdit(_oldJson)
            .AsReadOnly(true).WithLineNumbers(false)
            .WithSyntaxHighlighter(new JsonSyntaxHighlighter())
            .WithVerticalAlignment(VerticalAlignment.Fill).NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).Build();
        var updated = Controls.MultilineEdit(_newJson)
            .AsReadOnly(true).WithLineNumbers(false)
            .WithSyntaxHighlighter(new JsonSyntaxHighlighter())
            .WithVerticalAlignment(VerticalAlignment.Fill).NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).Build();

        var grid = Controls.HorizontalGrid().WithAlignment(HorizontalAlignment.Stretch).WithMargin(2, 1, 2, 0)
            .Column(c => c.Flex(1.0).Add(current))
            .Column(c => c.Width(1))
            .Column(c => c.Flex(1.0).Add(updated))
            .Build();
        Modal.AddControl(grid);

        var apply = Controls.Button(" Apply (Enter) ").Build();
        apply.Click += (_, _) => CloseWithResult(true);
        var cancel = Controls.Button(" Cancel (Esc) ").Build();
        cancel.Click += (_, _) => CloseWithResult(false);
        Modal.AddControl(Controls.HorizontalGrid().WithAlignment(HorizontalAlignment.Center).StickyBottom()
            .Column(c => c.Add(apply)).Column(c => c.Width(2)).Column(c => c.Add(cancel)).Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter) { CloseWithResult(true); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; }
    }
}
