// -----------------------------------------------------------------------
// LazyCaddy - the single queryable source of palette commands. Holds registered
// commands, fuzzy-searches them (prefix-boosted substring over label/category/
// keybinding, priority tiebreak), and maps key combos to commands.
//
// Pure (no UI/HTTP) so it's unit-testable. Search algorithm mirrors LazyDotIde's.
// -----------------------------------------------------------------------

namespace LazyCaddy.Services;

public sealed class CommandRegistry
{
    private readonly List<Command> _commands = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<(ConsoleKey, ConsoleModifiers), Command> _keyMap = new();

    /// <summary>Register a command. Ids are stable handles and must be unique.</summary>
    public void Register(Command command)
    {
        if (!_ids.Add(command.Id))
            throw new InvalidOperationException($"Duplicate command id '{command.Id}'.");
        _commands.Add(command);
        if (command.KeyCombo is { } k)
            _keyMap[(k.Key, k.Modifiers)] = command;
    }

    public IReadOnlyList<Command> All => _commands;

    /// <summary>Look up a command bound to a key combo; null if none.</summary>
    public Command? FindByKey(ConsoleKey key, ConsoleModifiers modifiers)
        => _keyMap.TryGetValue((key, modifiers), out var c) ? c : null;

    /// <summary>Filter by substring across label/category/keybinding (case-insensitive),
    /// ordered with prefix-of-label matches first, then by descending priority. Empty query
    /// returns all commands by priority.</summary>
    public List<Command> Search(string query)
    {
        var q = query?.Trim() ?? "";
        var filtered = q.Length == 0
            ? _commands.ToList()
            : _commands.Where(c =>
                c.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (c.Keybinding is { } kb && kb.Contains(q, StringComparison.OrdinalIgnoreCase)))
              .ToList();

        return filtered
            .OrderByDescending(c => q.Length > 0 && c.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(c => c.Priority)
            .ToList();
    }
}
