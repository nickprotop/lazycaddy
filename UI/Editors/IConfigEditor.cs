using SharpConsoleUI.Controls;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

/// <summary>
/// A presentation-agnostic editor for one config node, shown as a tab in the consolidated route
/// modal. It builds controls into a tab container, loads its node, tracks dirtiness, and produces
/// PendingWrites — it NEVER writes (the modal owns a single batched Apply).
/// </summary>
public interface IConfigEditor
{
    string TabTitle { get; }
    string ConfigPath { get; }
    /// <summary>Add controls into the tab's container (any linear-child host — the modal passes a
    /// ScrollablePanel). onDirtyChanged lets the modal update its dirty indicator / tab marker when
    /// a control value changes (best-effort).</summary>
    void Build(IControlHost container, Action onDirtyChanged);
    /// <summary>GET the node and populate controls; capture the loaded snapshot for dirty + diff.</summary>
    Task LoadAsync(EditCoordinator editor);
    bool IsDirty { get; }
    /// <summary>The pending writes for this tab if dirty, else empty (0..n). The modal flattens
    /// every tab's list into one batch.</summary>
    IReadOnlyList<PendingWrite> BuildPatch();
    /// <summary>Repopulate controls from the loaded snapshot.</summary>
    void Revert();
    /// <summary>Editor-specific keys (e.g. upstream-list Del). NOT apply (the modal owns apply). True if consumed.</summary>
    bool HandleKey(ConsoleKeyInfo key);
}
