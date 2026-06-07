// -----------------------------------------------------------------------
// LazyCaddy - route detail overlay.
//
// Opened from the Routes table on row activation. Shows the route's full
// matcher/handler config pretty-printed, with JSON syntax highlighting,
// in a read-only viewer. Esc closes.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class RouteDetailModal : ModalBase<bool>
{
    private readonly Route _route;

    private RouteDetailModal(Route route) => _route = route;

    public static Task<bool> ShowAsync(ConsoleWindowSystem windowSystem, Route route, Window? parent = null)
    {
        var instance = new RouteDetailModal(route);
        return ((ModalBase<bool>)instance).ShowAsync(windowSystem, parent);
    }

    protected override string GetTitle() => $" Route — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (84, 28);

    protected override void BuildContent()
    {
        var tls = _route.TlsEnabled ? "[" + UIConstants.Good.ToMarkup() + "]on[/]" : "[" + UIConstants.MutedText.ToMarkup() + "]off[/]";
        var muted = UIConstants.MutedText.ToMarkup();
        var accent = UIConstants.Accent.ToMarkup();

        var header = Controls.Markup()
            .AddLine($"[bold {accent}]{Escape(_route.HostOrMatch)}[/]")
            .AddEmptyLine()
            .AddLine($"[{muted}]Upstream[/]  {Escape(_route.Upstream)}")
            .AddLine($"[{muted}]TLS[/]       {tls}      [{muted}]Status[/]  {UIConstants.StatusMarkup(_route.Status)}")
            .AddEmptyLine()
            .WithMargin(2, 1, 2, 0)
            .Build();
        Modal.AddControl(header);

        var rule = Controls.RuleBuilder()
            .WithTitle("Config")
            .WithColor(UIConstants.AccentBlue)
            .WithMargin(2, 0, 2, 0)
            .Build();
        Modal.AddControl(rule);

        var viewer = Controls.MultilineEdit(_route.RawConfigJson)
            .AsReadOnly(true)
            .WithLineNumbers(true)
            .WithSyntaxHighlighter(new JsonSyntaxHighlighter())
            .WithViewportHeight(16)
            .WithMargin(2, 1, 2, 0)
            .Build();
        Modal.AddControl(viewer);

        var hint = Controls.Markup()
            .AddLine($"[{muted}]Esc:Close[/]")
            .WithMargin(2, 0, 2, 0)
            .StickyBottom()
            .Build();
        Modal.AddControl(hint);
    }

    // Escape Spectre markup metacharacters in dynamic text.
    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
