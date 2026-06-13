// -----------------------------------------------------------------------
// LazyCaddy - generates the "Keyboard shortcuts" help topic live from the
// CommandRegistry, so it never drifts from the actual commands. Pure: registry
// in, Markdown out.
// -----------------------------------------------------------------------

using System.Text;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Help;

public static class HelpKeysTopic
{
    /// <summary>Render all registered commands as a Markdown cheatsheet, grouped by category.</summary>
    public static string Render(CommandRegistry registry)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Keyboard shortcuts");
        sb.AppendLine();
        sb.AppendLine("Press **Ctrl+K** to open the command portal and run any of these by name.");
        sb.AppendLine();

        foreach (var group in registry.All
                     .GroupBy(c => c.Category)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("## ").AppendLine(group.Key);
            sb.AppendLine();
            foreach (var c in group.OrderByDescending(c => c.Priority).ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase))
            {
                var key = string.IsNullOrEmpty(c.Keybinding) ? "" : $" — `{c.Keybinding}`";
                sb.Append("- ").Append(c.Label).AppendLine(key);
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
