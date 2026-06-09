// -----------------------------------------------------------------------
// LazyCaddy - Logs: live access-log feed. Renders entries the shell's tail
// loop drains from LogState (applied on the UI thread). Top toolbar with
// Pause/Resume (Space), Errors-only (e), Clear (c); table filtering enabled.
// Capped ring of MaxRows; newest at bottom; auto-scroll unless paused.
//
// This view does NO file I/O and starts NO timer — DashboardShell owns the
// tail loop and calls ApplyNew() on the UI thread. The view sets LogState.IsActive
// in Build so the loop only reads while this view is open. (NavigationView rebuilds
// content on reopen, so Build re-asserts IsActive and resets per-view UI state.)
// -----------------------------------------------------------------------

using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Models;
using LazyCaddy.Services;
using LazyCaddy.UI;

namespace LazyCaddy.Views;

public sealed class LogsView
{
    private readonly LogState _logState;
    private const int MaxRows = 500;

    private TableControl? _table;
    private ToolbarControl? _toolbar;
    private MarkupControl? _banner;
    private bool _paused;
    private bool _errorsOnly;

    public LogsView(LogState logState) => _logState = logState;

    public void SetActive(bool active) => _logState.IsActive = active;

    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (_table is null || _toolbar is null) return false;
        if (!_logState.IsActive) return false;
        switch (key.Key)
        {
            case ConsoleKey.Spacebar: TogglePause(); return true;
            case ConsoleKey.E: ToggleErrorsOnly(); return true;
            case ConsoleKey.C: ClearRows(); return true;
            default: return false;
        }
    }

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Logs[/]")
            .AddLine($"[{muted}]Live access-log feed. Tailed from Caddy's log file.[/]")
            .AddEmptyLine()
            .Build());

        _toolbar = ViewToolbar.Create("logsToolbar");
        panel.AddControl(_toolbar);

        _banner = Controls.Markup().WithMargin(2, 0, 2, 0).Build();
        panel.AddControl(_banner);

        _table = Controls.Table()
            .AddColumn("Time", TextJustification.Left, 10)
            .AddColumn("Stat", TextJustification.Center, 6)
            .AddColumn("Method", TextJustification.Left, 8)
            .AddColumn("Host", TextJustification.Left, 22)
            .AddColumn("URI", TextJustification.Left)
            .AddColumn("Dur", TextJustification.Right, 8)
            .AddColumn("Size", TextJustification.Right, 8)
            .Rounded()
            .WithBorderColor(UIConstants.MutedText)
            .Interactive()
            .WithFiltering()
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("logsTable")
            .Build();
        panel.AddControl(_table);

        _paused = false;
        _errorsOnly = false;
        _logState.IsActive = true;
        RebuildToolbar();
        UpdateBanner();
    }

    private void RebuildToolbar()
    {
        if (_toolbar is null) return;
        ViewToolbar.Rebuild(_toolbar, new ToolbarAction?[]
        {
            new(ViewToolbar.Caption("⏸", _paused ? "Resume" : "Pause", "Space"), TogglePause),
            new(ViewToolbar.Caption("⚠", _errorsOnly ? "All" : "Errors only", "e"), ToggleErrorsOnly),
            new(ViewToolbar.Caption("✕", "Clear", "c"), ClearRows),
        });
    }

    private void TogglePause() { _paused = !_paused; RebuildToolbar(); UpdateBanner(); }
    private void ToggleErrorsOnly() { _errorsOnly = !_errorsOnly; RebuildToolbar(); }
    private void ClearRows() { _table?.ClearRows(); }

    /// <summary>Called each poll (UI thread) like every view. The tail loop drives the live feed;
    /// here we just refresh the banner from the latest source/status.</summary>
    public void Update(DashboardState state) => UpdateBanner();

    /// <summary>Focus the logs table so its keys (pause/scroll) work immediately on view entry.</summary>
    public void FocusPrimary() => _table?.RequestFocus();

    /// <summary>Called by the shell's tail loop (marshalled to the UI thread) to apply newly
    /// tailed entries. No-op appending while paused (entries are still drained to bound memory).</summary>
    public void ApplyNew()
    {
        if (_table is null) return;
        UpdateBanner();

        var entries = _logState.Drain();
        if (_paused || entries.Count == 0) return;

        foreach (var e in entries)
        {
            if (_errorsOnly && !e.IsRaw && e.Status < 400) continue;
            AddRow(e);
        }
        while (_table.RowCount > MaxRows) _table.RemoveRow(0);
        if (_table.RowCount > 0) _table.SelectedRowIndex = _table.RowCount - 1;
    }

    private void AddRow(AccessLogEntry e)
    {
        if (_table is null) return;
        if (e.IsRaw)
        {
            _table.AddRow(new TableRow("", "", "", "", Escape(e.Raw ?? ""), "", ""));
            return;
        }
        _table.AddRow(new TableRow(
            e.Time.LocalDateTime.ToString("HH:mm:ss"),
            UIConstants.StatusMarkup(e.Status.ToString()),
            Escape(e.Method),
            Escape(e.Host),
            Escape(e.Uri),
            OverviewView.FormatMs(e.DurationSeconds),
            FormatSize(e.Size)));
    }

    private void UpdateBanner()
    {
        if (_banner is null) return;
        var muted = UIConstants.MutedText.ToMarkup();
        var bad = UIConstants.Bad.ToMarkup();
        var good = UIConstants.Good.ToMarkup();
        var accent = UIConstants.Accent.ToMarkup();
        var src = _logState.Source;
        var msg = src.Kind switch
        {
            LogSourceKind.Remote =>
                $"[{muted}]Remote Caddy — access logs are read locally. Pass [/][{accent}]--access-log <path>[/][{muted}] if the file is on this host.[/]",
            LogSourceKind.NotConfigured =>
                $"[{muted}]Caddy logs access to stderr, not a file. Add a file log writer (or pass [/][{accent}]--access-log[/][{muted}]).[/]",
            LogSourceKind.File when _logState.LastTail == TailKind.NotFound =>
                $"[{muted}]Found [/]{Escape(src.Path ?? "")}[{muted}] in config, but the file doesn't exist yet (no requests logged?).[/]",
            LogSourceKind.File when _logState.LastTail == TailKind.PermissionDenied =>
                $"[{bad}]▲[/] [{muted}]Found [/]{Escape(src.Path ?? "")}[{muted}], but it's not readable (permission denied). Run as a user in caddy's group, or pass [/][{accent}]--access-log[/][{muted}].[/]",
            LogSourceKind.File =>
                $"[{good}]▶[/] [{muted}]tailing [/]{Escape(src.Path ?? "")}" + (_paused ? $"  [{UIConstants.Warn.ToMarkup()}](paused)[/]" : ""),
            _ => "",
        };
        _banner.SetContent(new List<string> { msg });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes}B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0}K";
        return $"{bytes / (1024.0 * 1024.0):0.0}M";
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
