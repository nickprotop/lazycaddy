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
    private MarkupControl? _banner;

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
            .AddLine($"[{muted}]Certificate health. Soonest-to-expire first; days-left color-coded by urgency.[/]")
            .AddEmptyLine()
            .Build());

        _banner = Controls.Markup().WithMargin(2, 0, 2, 0).Build();
        panel.AddControl(_banner);

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
        var sorted = CertExpiry.SortByUrgency(snap.Certs); // soonest-expiry first
        UpdateBanner(CertExpiry.Summarize(sorted, now));

        int prev = _table.SelectedRowIndex;
        _table.ClearRows();
        var muted = UIConstants.MutedText.ToMarkup();
        foreach (var c in sorted)
        {
            var expires = c.ExpiryKnown ? c.Expires.ToString("yyyy-MM-dd") : "—";
            var daysLeft = c.ExpiryKnown ? UIConstants.DaysLeftMarkup(c.DaysLeft(now)) : $"[{muted}]unknown[/]";
            _table.AddRow(new TableRow(
                Escape(c.Domain),
                Escape(c.Issuer),
                expires,
                daysLeft,
                UIConstants.StatusMarkup(c.AcmeStatus))
            { Tag = c });
        }
        if (sorted.Count > 0)
            _table.SelectedRowIndex = prev >= 0 && prev < sorted.Count ? prev : 0;
        RebuildToolbar();
    }

    // A one-line health banner: green "all healthy" when nothing's due, else a red/amber
    // count of expired / critical (<14d) / expiring (<30d) certs.
    private void UpdateBanner(CertHealth h)
    {
        if (_banner is null) return;
        var muted = UIConstants.MutedText.ToMarkup();

        if (h.Total == 0) { _banner.SetContent(new List<string> { $"[{muted}]No certificates managed by this Caddy instance.[/]" }); return; }

        // The unknown-expiry note (appended to whatever the alert/healthy line says).
        var unknownNote = h.Unknown > 0
            ? $"   [{muted}]· {h.Unknown} expiry unknown (read on the Caddy host)[/]"
            : "";

        if (!h.HasAlert)
        {
            var line = h.Ok > 0
                ? $"[{UIConstants.Good.ToMarkup()}]●[/] [{muted}]{h.Ok} certificate(s) healthy (≥{CertExpiry.WarningDays}d).[/]"
                : $"[{muted}]No expiry data available.[/]";
            _banner.SetContent(new List<string> { line + unknownNote });
            return;
        }

        var parts = new List<string>();
        if (h.Expired > 0) parts.Add($"[{UIConstants.Bad.ToMarkup()}]{h.Expired} expired[/]");
        if (h.Critical > 0) parts.Add($"[{UIConstants.Bad.ToMarkup()}]{h.Critical} critical (<{CertExpiry.CriticalDays}d)[/]");
        if (h.Warning > 0) parts.Add($"[{UIConstants.Warn.ToMarkup()}]{h.Warning} expiring (<{CertExpiry.WarningDays}d)[/]");
        var icon = h.HasCritical ? $"[{UIConstants.Bad.ToMarkup()}]▲[/]" : $"[{UIConstants.Warn.ToMarkup()}]▲[/]";
        _banner.SetContent(new List<string> { $"{icon} {string.Join($"[{muted}],[/] ", parts)}" + unknownNote });
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
