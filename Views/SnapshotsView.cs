// -----------------------------------------------------------------------
// LazyCaddy - Snapshots: config history table. Enter previews+restores;
// 'p' pins/unpins. Snapshots are auto-captured before each edit.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Models;
using LazyCaddy.Services;
using LazyCaddy.UI;
using LazyCaddy.UI.Modals;

namespace LazyCaddy.Views;

public sealed class SnapshotsView : ICommandProvider
{
    private readonly ConsoleWindowSystem _ws;
    private readonly EditCoordinator _editor;
    private TableControl? _table;
    private ToolbarControl? _toolbar;

    public SnapshotsView(ConsoleWindowSystem ws, EditCoordinator editor) { _ws = ws; _editor = editor; }

    public object? SelectedTag => _table?.SelectedRow?.Tag;

    public IEnumerable<Command> GetCommands()
    {
        const int idx = 6;
        bool onView(CommandContext c) => c.CurrentViewIndex == idx;
        bool onRow(CommandContext c) => onView(c) && c.SelectedTag is Snapshot;
        string rowReason(CommandContext c) => onView(c) ? "select a snapshot first" : "go to Snapshots";

        yield return new Command
        {
            Id = "snapshots.restore", Label = "Restore snapshot", Category = "Snapshots", Icon = "↩",
            Keybinding = "Enter", Priority = 64,
            CanExecute = onRow, DisabledReason = rowReason,
            Execute = ctx => { _ = RestoreSelectedAsync(); },
        };
        yield return new Command
        {
            Id = "snapshots.pin", Label = "Pin / unpin snapshot", Category = "Snapshots", Icon = "⊙",
            Keybinding = "p", Priority = 62,
            CanExecute = onRow, DisabledReason = rowReason,
            Execute = _ => PinSelected(),
        };
    }

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Snapshots[/]")
            .AddLine($"[{muted}]Config history. Auto-captured before each edit.[/]")
            .AddEmptyLine().Build());

        _toolbar = ViewToolbar.Create("snapshotsToolbar");
        panel.AddControl(_toolbar);

        _table = Controls.Table()
            .AddColumn("Time", TextJustification.Left, 22)
            .AddColumn("Description", TextJustification.Left)
            .AddColumn("Pinned", TextJustification.Center, 8)
            .Rounded().WithBorderColor(UIConstants.MutedText)
            .Interactive().WithSorting().WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("snapshotsTable").Build();

        _table.RowActivatedAsync += async (_, _) => await RestoreSelectedAsync();
        _table.SelectedRowChanged += (_, _) => RebuildToolbar();

        panel.AddControl(_table);
        Refresh();
        RebuildToolbar();
    }

    private void RebuildToolbar()
    {
        if (_toolbar is null) return;
        // Restore/Pin are no-ops without a selected snapshot; Snapshot-now always works.
        ViewToolbar.Rebuild(_toolbar, new ToolbarAction?[]
        {
            new(ViewToolbar.Caption("⊕", "Snapshot now", "S"), () => _ = SnapshotNowAsync()),
            new(ViewToolbar.Caption("↩", "Restore", "Enter"), () => _ = RestoreSelectedAsync()),
            new(ViewToolbar.Caption("⊙", "Pin", "p"), PinSelected),
        });
    }

    // ── Shared handlers (invoked by both keys/activation and toolbar buttons) ──

    private async Task RestoreSelectedAsync()
    {
        if (_table?.SelectedRow?.Tag is not Snapshot s) return;
        await SnapshotPickerDialog.ShowAsync(_ws, s, _editor);
        Refresh();
    }

    private void PinSelected()
    {
        if (_table?.SelectedRow?.Tag is not Snapshot s) return;
        _editor.Snapshots.Pin(s.Id, !s.Pinned);
        Refresh();
    }

    /// <summary>Capture the current running config under a user-supplied label.
    /// Mirrors the shell's Shift+S behavior.</summary>
    private async Task SnapshotNowAsync()
    {
        var label = await SnapshotNowDialog.ShowAsync(_ws);
        if (label is null) return; // cancelled
        try
        {
            var cfg = await _editor.GetRawConfigAsync();
            _editor.Snapshots.Capture(cfg, label);
        }
        catch { /* ignore: snapshot is best-effort */ }
        Refresh();
    }

    /// <summary>Reload rows from the snapshot store. Call on open and after edits/restores.</summary>
    public void Refresh()
    {
        if (_table is null) return;
        int prev = _table.SelectedRowIndex;
        _table.ClearRows();
        var all = _editor.Snapshots.All();
        foreach (var s in all)
        {
            _table.AddRow(new TableRow(
                s.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                Esc(s.Label ?? ""),
                s.Pinned ? $"[{UIConstants.Accent.ToMarkup()}]●[/]" : "")
            { Tag = s });
        }
        if (all.Count > 0)
            _table.SelectedRowIndex = prev >= 0 && prev < all.Count ? prev : 0;
        RebuildToolbar();
    }

    public void Update(DashboardState state) { /* snapshot data is independent of the poll */ }

    /// <summary>Focus the snapshots table so its keys work immediately on view entry.</summary>
    public void FocusPrimary() => _table?.RequestFocus();

    /// <summary>'p' pins/unpins the selected snapshot when the table has focus.</summary>
    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (_table is null || !_table.HasFocus) return false;
        if (key.Key == ConsoleKey.P)
        {
            PinSelected();
            return true;
        }
        return false;
    }

    private static string Esc(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
