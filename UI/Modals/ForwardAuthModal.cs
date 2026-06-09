// -----------------------------------------------------------------------
// LazyCaddy - ForwardAuthModal: pick an external-auth provider + upstream for a
// forward_auth handler. Returns the chosen provider/upstream, or null on cancel.
// RoutesView builds SecurityHandlerPatch.ForwardAuth(...) from the result and
// splices it before the proxy. Modeled on UpstreamDialog's prompt scaffolding.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

/// <summary>The user's forward_auth choice: provider preset + auth upstream (host:port).</summary>
public readonly record struct ForwardAuthChoice(ForwardAuthProvider Provider, string Upstream);

public sealed class ForwardAuthModal : ModalBase<ForwardAuthChoice?>
{
    private DropdownControl? _provider;
    private PromptControl? _upstream;
    private MarkupControl? _error;

    private static readonly (string Label, ForwardAuthProvider Provider)[] Providers =
    {
        ("Authelia",  ForwardAuthProvider.Authelia),
        ("Authentik", ForwardAuthProvider.Authentik),
        ("Custom",    ForwardAuthProvider.Custom),
    };

    private ForwardAuthModal() { }

    public static new Task<ForwardAuthChoice?> ShowAsync(ConsoleWindowSystem ws, Window? parent = null)
        => ((ModalBase<ForwardAuthChoice?>)new ForwardAuthModal()).ShowAsync(ws, parent);

    protected override string GetTitle() => " Forward auth ";
    protected override (int width, int height) GetSize() => (66, 11);
    protected override ForwardAuthChoice? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Protects this route via an external auth service (Authelia/Authentik). Inserted before the proxy.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _provider = Controls.Dropdown("Provider:        ").AddItems(Providers.Select(p => p.Label).ToArray()).Build();
        _upstream = Controls.Prompt("Auth upstream (host:port): ").WithInputWidth(30).Build();
        Modal.AddControl(_provider);
        Modal.AddControl(_upstream);

        _error = Controls.Markup().WithMargin(2, 0, 2, 0).Build();
        Modal.AddControl(_error);

        var ok = Controls.Button(" OK (Enter) ").Build(); ok.Click += (_, _) => Submit();
        var cancel = Controls.Button(" Cancel (Esc) ").Build(); cancel.Click += (_, _) => CloseWithResult(null);
        Modal.AddControl(Controls.HorizontalGrid().WithAlignment(HorizontalAlignment.Center).StickyBottom()
            .Column(c => c.Add(ok)).Column(c => c.Width(2)).Column(c => c.Add(cancel)).Build());
    }

    private void Submit()
    {
        var upstream = (_upstream?.Input ?? "").Trim();
        if (upstream.Length == 0)
        {
            _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]Auth upstream is required.[/]" });
            return;
        }
        var idx = _provider?.SelectedIndex ?? 0;
        if (idx < 0 || idx >= Providers.Length) idx = 0;
        CloseWithResult(new ForwardAuthChoice(Providers[idx].Provider, upstream));
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter) { Submit(); e.Handled = true; }
        else if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(null); e.Handled = true; }
    }
}
