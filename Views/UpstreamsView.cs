// -----------------------------------------------------------------------
// LazyCaddy - Upstreams: one row per distinct upstream, with active-probe
// reachability + latency and an inline spinner frame while a probe is pending.
// -----------------------------------------------------------------------

using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Models;

namespace LazyCaddy.Views;

public sealed class UpstreamsView
{
    // Braille spinner frames advanced on each refresh while a probe is in flight.
    private static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    private TableControl? _table;
    private int _spinnerTick;
    private string? _renderedSignature;   // skip rebuild when settled + unchanged (see Update)

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Upstreams[/]")
            .AddLine($"[{muted}]Reachability is an active TCP probe run on the background thread.[/]")
            .AddEmptyLine()
            .Build());

        _table = Controls.Table()
            .AddColumn("Upstream", TextJustification.Left)
            .AddColumn("Reachable", TextJustification.Left, 16)
            .AddColumn("Latency", TextJustification.Right, 12)
            .AddColumn("Used by routes", TextJustification.Left)
            .Rounded()
            .WithBorderColor(UIConstants.MutedText)
            .Interactive()
            .WithSorting()
            .WithFiltering()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("upstreamsTable")
            .Build();

        panel.AddControl(_table);
        _renderedSignature = null;   // fresh table on reopen — force first Update to populate
    }

    /// <summary>Focus the upstreams table so its keys work immediately on view entry.</summary>
    public void FocusPrimary() => _table?.RequestFocus();

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null || _table is null) return;

        _spinnerTick++;
        var spinner = SpinnerFrames[_spinnerTick % SpinnerFrames.Length];

        // Skip the per-tick ClearRows()+rebuild when nothing changed — avoids flicker and wasted
        // work each poll. BUT the reachability cell animates a spinner while a probe is in flight,
        // so only skip when every upstream has settled (Up/Down); keep rebuilding while probing.
        bool anyProbing = snap.Upstreams.Any(u =>
            u.Reachability is UpstreamReachability.Probing or UpstreamReachability.Unknown);
        var sig = string.Join("\n", snap.Upstreams.Select(u =>
            $"{u.Address}|{u.Reachability}|{u.Latency?.TotalMilliseconds:F0}|{string.Join(",", u.UsedByRoutes)}"));
        if (!anyProbing && sig == _renderedSignature) return;
        _renderedSignature = sig;

        _table.ClearRows();
        foreach (var u in snap.Upstreams)
        {
            _table.AddRow(
                Escape(u.Address),
                ReachabilityCell(u, spinner),
                LatencyCell(u),
                Escape(string.Join(", ", u.UsedByRoutes)));
        }
    }

    private static string ReachabilityCell(Upstream u, string spinner) => u.Reachability switch
    {
        UpstreamReachability.Up => $"[{UIConstants.Good.ToMarkup()}]● up[/]",
        UpstreamReachability.Down => $"[{UIConstants.Bad.ToMarkup()}]● down[/]",
        UpstreamReachability.Probing => $"[{UIConstants.Warn.ToMarkup()}]{spinner} probing[/]",
        _ => $"[{UIConstants.MutedText.ToMarkup()}]{spinner} …[/]",
    };

    private static string LatencyCell(Upstream u) =>
        u.Latency is { } l
            ? $"[{UIConstants.MutedText.ToMarkup()}]{l.TotalMilliseconds:F0} ms[/]"
            : $"[{UIConstants.MutedText.ToMarkup()}]—[/]";

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
