using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

/// <summary>Pick a handler type to add. Returns the JSON type (or "redir"), or null on cancel.</summary>
public sealed class HandlerTypePicker : ModalBase<string?>
{
    private DropdownControl? _type;
    private MarkupControl? _error;

    private HandlerTypePicker() { }

    public static new Task<string?> ShowAsync(ConsoleWindowSystem ws, Window? parent = null)
        => ((ModalBase<string?>)new HandlerTypePicker()).ShowAsync(ws, parent);

    protected override string GetTitle() => " Add handler ";
    protected override (int width, int height) GetSize() => (60, 9);
    protected override string? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Choose a handler type to append to this route.[/]").WithMargin(2, 1, 2, 0).Build());
        _type = Controls.Dropdown("Handler:  ")
            .AddItems(NewRouteSkeleton.OfferedTypes.Select(t => $"{t.Icon} {t.DisplayName}").ToArray()).Build();
        Modal.AddControl(_type);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: add   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(null); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            e.Handled = true;
            var idx = _type?.SelectedIndex ?? -1;
            if (idx < 0 || idx >= NewRouteSkeleton.OfferedTypes.Count)
            { _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]Pick a type.[/]" }); return; }
            CloseWithResult(NewRouteSkeleton.OfferedTypes[idx].Type);
        }
    }
}
