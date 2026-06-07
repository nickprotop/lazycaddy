// -----------------------------------------------------------------------
// LazyCaddy - shared helper for per-view action toolbars.
//
// Builds a borderless, transparent toolbar of clickable buttons whose captions
// embed the keyboard shortcut in dim markup (e.g. "✎ Upstream [grey50]e[/]"),
// mirroring the cxfiles toolbar style. Toolbars are ADAPTIVE: a view calls
// Rebuild(...) whenever its context changes (e.g. row selected/deselected) and
// supplies only the buttons valid right now.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;

namespace LazyCaddy.UI;

/// <summary>One toolbar action: caption already contains its [grey50]shortcut[/] hint.</summary>
public readonly record struct ToolbarAction(string Caption, Action OnClick);

public static class ViewToolbar
{
    private static readonly string Shortcut = UIConstants.MutedText.ToMarkup();

    /// <summary>Format a button caption: "icon Label [muted]key[/]".</summary>
    public static string Caption(string icon, string label, string key)
        => $"{icon} {label} [{Shortcut}]{key}[/]";

    /// <summary>Create an empty toolbar control to place at the top of a view.</summary>
    public static ToolbarControl Create(string name)
        => Controls.Toolbar()
            .StickyTop()
            .WithSpacing(1)
            .WithWrap()
            .WithMargin(0, 0, 0, 0)
            .WithBackgroundColor(Color.Transparent)
            .WithBelowLineColor(UIConstants.BorderSubtle)
            .WithName(name)
            .Build();

    /// <summary>Replace the toolbar's buttons with the given actions (adaptive rebuild).
    /// Separators between groups are inserted where an action's Caption is null/empty.</summary>
    public static void Rebuild(ToolbarControl toolbar, IEnumerable<ToolbarAction?> actions)
    {
        toolbar.Clear();
        foreach (var a in actions)
        {
            if (a is not { } action) { toolbar.AddItem(new SeparatorControl()); continue; }
            toolbar.AddItem(Button(action));
        }
    }

    private static ButtonControl Button(ToolbarAction a)
        => Controls.Button()
            .WithText(a.Caption)
            .WithBorder(ButtonBorderStyle.None)
            .WithBackgroundColor(Color.Transparent)
            .WithBorderBackgroundColor(Color.Transparent)
            .OnClick((_, _) => a.OnClick())
            .Build();
}
