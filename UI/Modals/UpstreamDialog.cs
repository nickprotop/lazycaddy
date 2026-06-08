// -----------------------------------------------------------------------
// LazyCaddy - tiny prompt to add or edit a reverse_proxy upstream dial
// (host:port). Returns the trimmed string, or null if cancelled / empty.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;

namespace LazyCaddy.UI.Modals;

public sealed class UpstreamDialog : ModalBase<string?>
{
    private readonly string _title;
    private readonly string _initial;
    private PromptControl? _dial;

    private UpstreamDialog(string title, string initial) { _title = title; _initial = initial; }

    /// <summary>Prompt for an upstream dial. <paramref name="initial"/> seeds the field (empty for Add).
    /// Returns the trimmed dial, or null if cancelled or left empty.</summary>
    public static Task<string?> ShowAsync(ConsoleWindowSystem ws, string title, string initial = "", Window? parent = null)
        => ((ModalBase<string?>)new UpstreamDialog(title, initial)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" {_title} ";
    protected override (int width, int height) GetSize() => (60, 9);
    protected override string? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{UIConstants.MutedText.ToMarkup()}]Backend address as host:port (e.g. 127.0.0.1:8080).[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _dial = Controls.Prompt("Dial: ").WithInput(_initial).WithInputWidth(44).Build();
        Modal.AddControl(_dial);

        var ok = Controls.Button(" OK (Enter) ").Build(); ok.Click += (_, _) => Submit();
        var cancel = Controls.Button(" Cancel (Esc) ").Build(); cancel.Click += (_, _) => CloseWithResult(null);
        Modal.AddControl(Controls.HorizontalGrid().WithAlignment(HorizontalAlignment.Center).StickyBottom()
            .Column(c => c.Add(ok)).Column(c => c.Width(2)).Column(c => c.Add(cancel)).Build());
    }

    private void Submit()
    {
        var v = (_dial?.Input ?? "").Trim();
        CloseWithResult(v.Length == 0 ? null : v);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter) { Submit(); e.Handled = true; }
        else if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(null); e.Handled = true; }
    }
}
