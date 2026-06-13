using LazyCaddy.Services;
using LazyCaddy.UI.Help;
using Xunit;

namespace LazyCaddy.Tests;

/// <summary>
/// The "Keyboard shortcuts" help topic is generated live from the CommandRegistry so it never
/// drifts from the actual commands. HelpKeysTopic.Render groups commands by category and lists
/// each command's label + keybinding as Markdown.
/// </summary>
public class HelpKeysTopicTests
{
    private static CommandRegistry Reg(params Command[] cmds)
    {
        var r = new CommandRegistry();
        foreach (var c in cmds) r.Register(c);
        return r;
    }

    [Fact]
    public void Render_GroupsByCategory_WithHeadings()
    {
        var md = HelpKeysTopic.Render(Reg(
            new Command { Id = "a", Label = "Go to Routes", Category = "Go", Keybinding = "2" },
            new Command { Id = "b", Label = "Refresh", Category = "Global", Keybinding = "R" }));

        Assert.Contains("# Keyboard shortcuts", md);
        Assert.Contains("## Go", md);
        Assert.Contains("## Global", md);
    }

    [Fact]
    public void Render_ListsLabelAndKeybinding()
    {
        var md = HelpKeysTopic.Render(Reg(
            new Command { Id = "a", Label = "Refresh", Category = "Global", Keybinding = "R" }));

        Assert.Contains("Refresh", md);
        Assert.Contains("R", md);
    }

    [Fact]
    public void Render_CommandWithoutKeybinding_StillListed()
    {
        var md = HelpKeysTopic.Render(Reg(
            new Command { Id = "a", Label = "Adapt Caddyfile", Category = "Global" }));

        Assert.Contains("Adapt Caddyfile", md);
    }

    [Fact]
    public void Render_EmptyRegistry_ProducesHeadingOnly()
    {
        var md = HelpKeysTopic.Render(Reg());

        Assert.Contains("# Keyboard shortcuts", md);
    }
}
