// -----------------------------------------------------------------------
// LazyCaddy - TLS/Certs: one row per certificate, with a threshold-colored
// "Days left" cell (red < 14, yellow < 30, green otherwise).
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

public sealed class CertsView
{
    private readonly ConsoleWindowSystem _ws;
    private readonly EditCoordinator _editor;

    private TableControl? _table;
    private ToolbarControl? _toolbar;

    public CertsView(ConsoleWindowSystem ws, EditCoordinator editor) { _ws = ws; _editor = editor; }

    /// <summary>Handle a view-level edit shortcut. Returns true if consumed.
    /// Only acts when this view's table currently has focus.</summary>
    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (_table is null || !_table.HasFocus) return false;
        if (key.Key == ConsoleKey.E)
        {
            EditTls();
            return true;
        }
        return false;
    }

    // Shared handler for both the 'e' key and the TLS toolbar button. No-ops without a row.
    private void EditTls()
    {
        if (_table?.SelectedRow?.Tag is Cert cert)
            _ = EditTlsPolicyDialog.ShowAsync(_ws, cert, _editor);
    }

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]TLS / Certs[/]")
            .AddLine($"[{muted}]Certificate health. Days-left is color-coded by urgency.[/]")
            .AddEmptyLine()
            .Build());

        _toolbar = ViewToolbar.Create("certsToolbar");
        panel.AddControl(_toolbar);

        _table = Controls.Table()
            .AddColumn("Domain", TextJustification.Left)
            .AddColumn("Issuer", TextJustification.Left)
            .AddColumn("Expires", TextJustification.Left, 12)
            .AddColumn("Days left", TextJustification.Right, 16)
            .AddColumn("ACME", TextJustification.Left, 10)
            .Rounded()
            .WithBorderColor(UIConstants.MutedText)
            .Interactive()
            .WithSorting()
            .WithFiltering()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("certsTable")
            .Build();

        _table.SelectedRowChanged += (_, _) => RebuildToolbar();
        // Enter / double-click on a cert row opens the TLS policy editor (parity with Routes).
        _table.RowActivatedAsync += async (_, _) => { EditTls(); await Task.CompletedTask; };

        panel.AddControl(_table);
        RebuildToolbar();
    }

    private void RebuildToolbar()
    {
        if (_toolbar is null) return;
        // Always shown; no-ops without a selected cert.
        ViewToolbar.Rebuild(_toolbar, new ToolbarAction?[]
        {
            new(ViewToolbar.Caption("🔒", "TLS", "e"), EditTls),
        });
    }

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null || _table is null) return;

        var now = snap.Timestamp;
        int prev = _table.SelectedRowIndex;
        _table.ClearRows();
        foreach (var c in snap.Certs)
        {
            _table.AddRow(new TableRow(
                Escape(c.Domain),
                Escape(c.Issuer),
                c.Expires.ToString("yyyy-MM-dd"),
                UIConstants.DaysLeftMarkup(c.DaysLeft(now)),
                UIConstants.StatusMarkup(c.AcmeStatus))
            { Tag = c });
        }
        if (snap.Certs.Count > 0)
            _table.SelectedRowIndex = prev >= 0 && prev < snap.Certs.Count ? prev : 0;
        RebuildToolbar();
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
