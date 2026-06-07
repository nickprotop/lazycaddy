// -----------------------------------------------------------------------
// LazyCaddy - Snapshots: config history table. Enter previews+restores;
// 'p' pins/unpins. Snapshots are auto-captured before each edit.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Models;
using LazyCaddy.Services;
using LazyCaddy.UI;
using LazyCaddy.UI.Modals;

namespace LazyCaddy.Views;

public sealed class SnapshotsView
{
    private readonly ConsoleWindowSystem _ws;
    private readonly EditCoordinator _editor;
    private TableControl? _table;
    private ToolbarControl? _toolbar;

    public SnapshotsView(ConsoleWindowSystem ws, EditCoordinator editor) { _ws = ws; _editor = editor; }

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Snapshots[/]")
            .AddLine($"[{muted}]Config history. Enter: preview/restore.  p: pin/unpin.  Auto-captured before each edit.[/]")
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
        // Both always shown; no-op without a selected snapshot.
        ViewToolbar.Rebuild(_toolbar, new ToolbarAction?[]
        {
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

    /// <summary>Reload rows from the snapshot store. Call on open and after edits/restores.</summary>
    public void Refresh()
    {
        if (_table is null) return;
        _table.ClearRows();
        foreach (var s in _editor.Snapshots.All())
        {
            _table.AddRow(new TableRow(
                s.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                Esc(s.Label ?? ""),
                s.Pinned ? $"[{UIConstants.Accent.ToMarkup()}]●[/]" : "")
            { Tag = s });
        }
        RebuildToolbar();
    }

    public void Update(DashboardState state) { /* snapshot data is independent of the poll */ }

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
