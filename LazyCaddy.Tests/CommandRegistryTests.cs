using LazyCaddy.Configuration;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

/// <summary>
/// CommandRegistry is the single queryable source of palette commands. It holds registered
/// commands, fuzzy-searches them (prefix-boosted substring, priority tiebreak), and maps key
/// combos to commands. Context-aware commands declare CanExecute/DisabledReason — pure data the
/// portal uses to dim disabled rows.
/// </summary>
public class CommandRegistryTests
{
    private static CommandContext Ctx(int viewIndex = 0, object? tag = null, bool hasSnapshots = false)
        => new(viewIndex, tag, hasSnapshots, Editor: null);

    private static Command Cmd(string id, string label, string category = "General", int priority = 50)
        => new() { Id = id, Label = label, Category = category, Priority = priority };

    [Fact]
    public void All_ReturnsEverythingRegistered()
    {
        var r = new CommandRegistry();
        r.Register(Cmd("a", "Alpha"));
        r.Register(Cmd("b", "Bravo"));

        Assert.Equal(new[] { "a", "b" }, r.All.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Register_DuplicateId_Throws()
    {
        var r = new CommandRegistry();
        r.Register(Cmd("dup", "First"));

        Assert.Throws<InvalidOperationException>(() => r.Register(Cmd("dup", "Second")));
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsAllByPriorityDescending()
    {
        var r = new CommandRegistry();
        r.Register(Cmd("low", "Low", priority: 10));
        r.Register(Cmd("high", "High", priority: 90));
        r.Register(Cmd("mid", "Mid", priority: 50));

        Assert.Equal(new[] { "high", "mid", "low" }, r.Search("").Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Search_SubstringMatchesLabelCategoryAndKeybinding()
    {
        var r = new CommandRegistry();
        r.Register(new Command { Id = "route", Label = "Edit match", Category = "Routes" });
        r.Register(new Command { Id = "snap", Label = "Snapshot now", Category = "Snapshots", Keybinding = "Shift+S" });
        r.Register(new Command { Id = "quit", Label = "Quit", Category = "Global" });

        Assert.Equal(new[] { "route" }, r.Search("match").Select(c => c.Id).ToArray());      // label
        Assert.Equal(new[] { "snap" }, r.Search("snapshots").Select(c => c.Id).ToArray());   // category
        Assert.Equal(new[] { "snap" }, r.Search("shift").Select(c => c.Id).ToArray());       // keybinding
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var r = new CommandRegistry();
        r.Register(Cmd("r", "Edit Match"));

        Assert.Single(r.Search("EDIT MATCH"));
        Assert.Single(r.Search("edit match"));
    }

    [Fact]
    public void Search_PrefixMatchesRankAboveNonPrefix_ThenPriority()
    {
        var r = new CommandRegistry();
        r.Register(Cmd("contains", "Go to Routes", priority: 99)); // "rout" not a prefix, high priority
        r.Register(Cmd("prefix", "Routes view", priority: 1));      // "rout" IS a prefix, low priority

        // Despite lower priority, the prefix match comes first.
        Assert.Equal(new[] { "prefix", "contains" }, r.Search("rout").Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var r = new CommandRegistry();
        r.Register(Cmd("a", "Alpha"));

        Assert.Empty(r.Search("zzz"));
    }

    [Fact]
    public void FindByKey_ReturnsCommandBoundToCombo()
    {
        var r = new CommandRegistry();
        var quit = new Command { Id = "quit", Label = "Quit", KeyCombo = new KeyBinding(ConsoleKey.Q) };
        r.Register(quit);
        r.Register(new Command { Id = "refresh", Label = "Refresh", KeyCombo = new KeyBinding(ConsoleKey.R) });

        Assert.Same(quit, r.FindByKey(ConsoleKey.Q, 0));
        Assert.Null(r.FindByKey(ConsoleKey.Z, 0));
    }

    [Fact]
    public void IsEnabled_NoPredicate_AlwaysEnabled()
    {
        var c = Cmd("a", "Always");

        Assert.True(c.IsEnabled(Ctx()));
        Assert.Null(c.GetDisabledReason(Ctx()));
    }

    [Fact]
    public void IsEnabled_ContextAware_ReflectsPredicate()
    {
        // "Edit match" enabled only when viewing Routes (index 2) with a string tag standing in for a Route.
        var c = new Command
        {
            Id = "edit-match",
            Label = "Edit match",
            CanExecute = ctx => ctx.CurrentViewIndex == 2 && ctx.SelectedTag is string,
            DisabledReason = ctx => ctx.CurrentViewIndex != 2 ? "go to Routes" : "select a route first",
        };

        Assert.True(c.IsEnabled(Ctx(viewIndex: 2, tag: "a-route")));
        Assert.False(c.IsEnabled(Ctx(viewIndex: 2, tag: null)));
        Assert.Equal("select a route first", c.GetDisabledReason(Ctx(viewIndex: 2, tag: null)));
        Assert.False(c.IsEnabled(Ctx(viewIndex: 0)));
        Assert.Equal("go to Routes", c.GetDisabledReason(Ctx(viewIndex: 0)));
    }
}
