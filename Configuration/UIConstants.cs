// -----------------------------------------------------------------------
// LazyCaddy - color palette and small markup helpers.
// Modeled on cxtop's Helpers/UIConstants.cs (deep blue-teal monitoring palette).
// -----------------------------------------------------------------------

using SharpConsoleUI;

namespace LazyCaddy.Configuration;

internal static class UIConstants
{
    #region Timing

    public const int FadeInMs = 250;

    #endregion

    #region Palette

    public static readonly Color BaseBg = new(0x0d, 0x11, 0x17);
    public static readonly Color BaseEnd = new(0x1a, 0x23, 0x32);
    public static readonly Color HeaderBg = new(0x0a, 0x0e, 0x14);
    public static readonly Color ContentBg = new(0x14, 0x19, 0x2d);

    public static readonly Color Accent = new(0x4e, 0xcd, 0xc4);   // teal
    public static readonly Color AccentBlue = new(0x64, 0xb4, 0xff);
    public static readonly Color PrimaryText = new(0xc8, 0xd4, 0xe0);
    public static readonly Color MutedText = new(0x4a, 0x60, 0x70);

    public static readonly Color Good = new(0x4e, 0xcd, 0xc4);     // green/teal
    public static readonly Color Warn = new(0xff, 0xd9, 0x3d);     // yellow
    public static readonly Color Bad = new(0xff, 0x6b, 0x6b);      // red

    public static readonly Color SelectedBg = new(0x28, 0x50, 0xa0);

    // Subtle, low-contrast frame color for the main window border (matches the
    // dark-neutral greys cxpost/LazyNuGet use: Grey27 / Grey23).
    public static readonly Color BorderSubtle = new(0x44, 0x44, 0x44);

    #endregion

    #region Markup helpers

    /// <summary>Green/red filled dot reflecting connection state.</summary>
    public static string ConnectionDot(bool connected) =>
        connected
            ? $"[{Good.ToMarkup()}]●[/]"
            : $"[{Bad.ToMarkup()}]●[/]";

    /// <summary>
    /// Wrap a cert "days left" count in threshold-colored markup:
    /// red &lt; 14, yellow &lt; 30, green otherwise.
    /// </summary>
    public static string DaysLeftMarkup(int days)
    {
        var color = days < 14 ? Bad : days < 30 ? Warn : Good;
        var text = days < 0 ? $"expired {-days}d ago" : $"{days}d";
        return $"[{color.ToMarkup()}]{text}[/]";
    }

    /// <summary>Color a generic up/down/ok/error status string.</summary>
    public static string StatusMarkup(string status)
    {
        var lower = status.ToLowerInvariant();
        var color = lower is "up" or "ok" or "active" or "valid" or "running" or "healthy"
            ? Good
            : lower is "down" or "error" or "failed" or "expired"
                ? Bad
                : Warn;
        return $"[{color.ToMarkup()}]{status}[/]";
    }

    #endregion
}
