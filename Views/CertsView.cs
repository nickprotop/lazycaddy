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
using LazyCaddy.UI.Modals;

namespace LazyCaddy.Views;

public sealed class CertsView
{
    private readonly ConsoleWindowSystem _ws;
    private readonly EditCoordinator _editor;

    private TableControl? _table;

    public CertsView(ConsoleWindowSystem ws, EditCoordinator editor) { _ws = ws; _editor = editor; }

    /// <summary>Handle a view-level edit shortcut. Returns true if consumed.
    /// Only acts when this view's table currently has focus.</summary>
    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (_table is null || !_table.HasFocus) return false;
        if (_table.SelectedRow?.Tag is not Cert cert) return false;
        if (key.Key == ConsoleKey.E)
        {
            _ = EditTlsPolicyDialog.ShowAsync(_ws, cert, _editor);
            return true;
        }
        return false;
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

        panel.AddControl(_table);
    }

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null || _table is null) return;

        var now = snap.Timestamp;
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
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
