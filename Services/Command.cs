// -----------------------------------------------------------------------
// LazyCaddy - command-portal model. A Command is one named, runnable action
// surfaced in the Ctrl+K palette. Context-aware commands declare CanExecute /
// DisabledReason so the portal can dim rows whose context isn't met.
//
// Modeled on the author's LazyDotIde CommandRegistry, plus a CanExecute predicate
// for LazyCaddy's row-dependent edits.
// -----------------------------------------------------------------------

namespace LazyCaddy.Services;

/// <summary>A structured key binding for routing/lookup (display + optional dispatch).</summary>
public sealed record KeyBinding(ConsoleKey Key, ConsoleModifiers Modifiers = 0);

/// <summary>Live shell state captured when the portal opens; passed to predicates and Execute.</summary>
public sealed record CommandContext(
    int CurrentViewIndex,
    object? SelectedTag,        // Route / Cert / Snapshot / HandlerDescriptor / log row / null
    bool HasSnapshots,
    EditCoordinator? Editor);

/// <summary>One palette command.</summary>
public sealed class Command
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Category { get; init; } = "General";
    public string Icon { get; init; } = "  ";
    public string? Keybinding { get; init; }          // display hint, e.g. "Ctrl+K"
    public KeyBinding? KeyCombo { get; init; }         // optional structured combo (lookup)
    public int Priority { get; init; } = 50;

    /// <summary>When null, the command is always enabled.</summary>
    public Func<CommandContext, bool>? CanExecute { get; init; }

    /// <summary>Human-readable reason shown when the command is disabled.</summary>
    public Func<CommandContext, string?>? DisabledReason { get; init; }

    public Action<CommandContext> Execute { get; init; } = _ => { };

    public bool IsEnabled(CommandContext ctx) => CanExecute is null || CanExecute(ctx);

    public string? GetDisabledReason(CommandContext ctx)
        => IsEnabled(ctx) ? null : (DisabledReason?.Invoke(ctx) ?? "unavailable here");
}

/// <summary>A view (or other component) that contributes commands to the registry.</summary>
public interface ICommandProvider
{
    IEnumerable<Command> GetCommands();
}
