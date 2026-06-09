// -----------------------------------------------------------------------
// LazyCaddy - Metrics: a deeper read of Caddy's /metrics than the Overview
// card. Request-rate sparkline, status-class breakdown bars, latency
// percentiles, in-flight gauge, and the busiest handlers.
//
// Everything degrades gracefully: caddy_http_requests_total is often absent
// until traffic flows through Caddy, and /metrics may be disabled entirely —
// in both cases the view shows a hint instead of empty charts.
// -----------------------------------------------------------------------

using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.Views;

public sealed class MetricsView
{
    private MarkupControl? _summary;
    private SparklineControl? _sparkline;
    private MarkupControl? _status;
    private MarkupControl? _latency;
    private TableControl? _topTable;

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Metrics[/]")
            .AddLine($"[{muted}]Live request metrics from Caddy's /metrics endpoint.[/]")
            .AddEmptyLine()
            .Build());

        _summary = Controls.Markup().WithMargin(2, 0, 2, 0).Build();
        panel.AddControl(_summary);

        panel.AddControl(Controls.RuleBuilder().WithTitle("Request rate (req/s)").WithColor(UIConstants.MutedText).Build());
        _sparkline = Controls.Sparkline()
            .WithHeight(7)
            .WithBarColor(UIConstants.AccentBlue)
            .WithData(Array.Empty<double>())
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithName("metricsSparkline")
            .Build();
        panel.AddControl(_sparkline);

        panel.AddControl(Controls.RuleBuilder().WithTitle("Status codes").WithColor(UIConstants.MutedText).Build());
        _status = Controls.Markup().WithMargin(2, 0, 2, 0).Build();
        panel.AddControl(_status);

        panel.AddControl(Controls.RuleBuilder().WithTitle("Latency").WithColor(UIConstants.MutedText).Build());
        _latency = Controls.Markup().WithMargin(2, 0, 2, 0).Build();
        panel.AddControl(_latency);

        panel.AddControl(Controls.RuleBuilder().WithTitle("Busiest handlers").WithColor(UIConstants.MutedText).Build());
        _topTable = Controls.Table()
            .AddColumn("Handler", TextJustification.Left, 24)
            .AddColumn("Requests", TextJustification.Right, 14)
            .AddColumn("Share", TextJustification.Left)
            .Rounded().WithBorderColor(UIConstants.MutedText)
            .WithName("metricsTopTable").Build();
        panel.AddControl(_topTable);
    }

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null) return;
        var m = snap.Metrics;
        var muted = UIConstants.MutedText.ToMarkup();

        if (!m.Available)
        {
            _summary?.SetContent(new List<string> { $"[{muted}]/metrics is not enabled on this Caddy instance.[/]" });
            _sparkline?.SetDataPoints(Array.Empty<double>());
            _status?.SetContent(new List<string> { "" });
            _latency?.SetContent(new List<string> { "" });
            _topTable?.ClearRows();
            return;
        }

        var rate = m.RequestRate.Count > 0 ? m.RequestRate[^1] : 0d;
        var sc = m.StatusClasses;
        _summary?.SetContent(new List<string>
        {
            $"[bold {UIConstants.Good.ToMarkup()}]{rate:0.0}[/] [{muted}]req/s[/]    " +
            $"[{muted}]in-flight[/] [bold]{m.InFlight:0}[/]    " +
            $"[{muted}]total requests[/] [bold]{sc.Total:0}[/]",
        });

        if (m.RequestRate.Count > 0) _sparkline?.SetDataPoints(m.RequestRate);

        // Status-code breakdown bars (share of total, color-coded by class).
        if (sc.Total <= 0)
        {
            _status?.SetContent(new List<string> { $"[{muted}]No requests recorded yet — traffic must flow through Caddy first.[/]" });
            _latency?.SetContent(new List<string> { $"[{muted}]—[/]" });
            _topTable?.ClearRows();
            return;
        }

        _status?.SetContent(new List<string>
        {
            Bar("2xx", sc.C2xx, sc.Total, UIConstants.Good),
            Bar("3xx", sc.C3xx, sc.Total, UIConstants.AccentBlue),
            Bar("4xx", sc.C4xx, sc.Total, UIConstants.Warn),
            Bar("5xx", sc.C5xx, sc.Total, UIConstants.Bad),
        });

        // Latency percentiles.
        if (m.Latency.Available)
        {
            _latency?.SetContent(new List<string>
            {
                $"[{muted}]p50[/] [bold]{OverviewView.FormatMs(m.Latency.P50)}[/]    " +
                $"[{muted}]p95[/] [bold]{OverviewView.FormatMs(m.Latency.P95)}[/]    " +
                $"[{muted}]p99[/] [bold]{OverviewView.FormatMs(m.Latency.P99)}[/]",
            });
        }
        else
        {
            _latency?.SetContent(new List<string> { $"[{muted}]latency histogram unavailable[/]" });
        }

        // Busiest handlers table.
        if (_topTable is not null)
        {
            _topTable.ClearRows();
            var handlerTotal = m.TopHandlers.Sum(h => h.Count);
            foreach (var h in m.TopHandlers)
            {
                var share = handlerTotal > 0 ? 100.0 * h.Count / handlerTotal : 0;
                _topTable.AddRow(new TableRow(Escape(h.Label), $"{h.Count:0}", MiniBar(share)));
            }
        }
    }

    // A labeled percentage bar, e.g. "2xx  ██████████░░░░  72%".
    private const int BarWidth = 24;
    private static string Bar(string label, double value, double total, SharpConsoleUI.Color color)
    {
        var pct = total > 0 ? value / total : 0;
        int filled = (int)Math.Round(pct * BarWidth);
        filled = Math.Clamp(filled, 0, BarWidth);
        var c = color.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        var bar = $"[{c}]{new string('█', filled)}[/][{muted}]{new string('░', BarWidth - filled)}[/]";
        return $"[{muted}]{label}[/]  {bar}  [bold]{pct * 100:0}%[/] [{muted}]({value:0})[/]";
    }

    // A compact share bar for the handlers table (no label/percent — column shows the count).
    private static string MiniBar(double sharePct)
    {
        int filled = Math.Clamp((int)Math.Round(sharePct / 100.0 * 16), 0, 16);
        var c = UIConstants.AccentBlue.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        return $"[{c}]{new string('█', filled)}[/][{muted}]{new string('░', 16 - filled)}[/] {sharePct:0}%";
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
