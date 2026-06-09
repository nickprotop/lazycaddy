// -----------------------------------------------------------------------
// LazyCaddy - Overview: a wrapping row of equal-size status cards + optional
// request-rate sparkline.
//
// Cards are rounded PanelControls that stretch to equal width and fill height
// (ServerHub btop-style), laid out left-to-right and wrapped onto additional
// rows when the content area is too narrow to fit them all. Each card's text
// is a markup string refreshed in place on each poll.
// -----------------------------------------------------------------------

using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;

namespace LazyCaddy.Views;

public sealed class OverviewView
{
    private readonly LazyCaddyConfig _config;

    // Constant card width; rows fit floor(contentWidth / CardWidth) cards each.
    private const int CardWidth = 30;

    private PanelControl? _caddyCard;
    private PanelControl? _sitesCard;
    private PanelControl? _certsCard;
    private PanelControl? _upstreamsCard;
    private PanelControl? _trafficCard;
    private SparklineControl? _sparkline;
    // Metrics detail (folded in from the former Metrics view): status-code bars,
    // latency percentiles, busiest-handlers table — shown below the sparkline.
    private MarkupControl? _statusBars;
    private MarkupControl? _latency;
    private TableControl? _topTable;

    // Resize/relayout state: the panel we live in, the cards, the row-grid controls
    // currently in the panel, the index they start at, and the last per-row count.
    private ScrollablePanelControl? _panel;
    private PanelControl[] _cards = Array.Empty<PanelControl>();
    private readonly List<IWindowControl> _rowGrids = new();
    private int _cardRowStartIndex;
    private int _lastPerRow = -1;

    public OverviewView(LazyCaddyConfig config) => _config = config;

    public void Build(ScrollablePanelControl panel)
    {
        _panel = panel;
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Overview[/]")
            .AddLine($"[{muted}]Live view of the running Caddy instance.[/]")
            .AddEmptyLine()
            .Build());

        _caddyCard = Card("Caddy");
        _sitesCard = Card("Sites");
        _certsCard = Card("Certs");
        _upstreamsCard = Card("Upstreams");
        _trafficCard = Card("Traffic");
        _cards = new[] { _caddyCard, _sitesCard, _certsCard, _upstreamsCard, _trafficCard };

        // Card rows are inserted starting here; on resize we remove and rebuild them
        // in place, leaving the header above and the sparkline below untouched.
        _cardRowStartIndex = panel.Children.Count;
        BuildCardRows(SafeConsoleWidth());

        if (_config.EnableRequestRateSparkline)
        {
            panel.AddControl(Controls.RuleBuilder()
                .WithTitle("Request rate")
                .WithColor(UIConstants.MutedText)
                .Build());

            _sparkline = Controls.Sparkline()
                .WithHeight(6)
                .WithBarColor(UIConstants.AccentBlue)
                .WithData(Array.Empty<double>())
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithName("overviewSparkline")
                .Build();
            panel.AddControl(_sparkline);
        }

        // ── Metrics detail (folded in from the former Metrics view) ──
        panel.AddControl(Controls.RuleBuilder().WithTitle("Status codes").WithColor(UIConstants.MutedText).Build());
        _statusBars = Controls.Markup().WithMargin(2, 0, 2, 0).Build();
        panel.AddControl(_statusBars);

        panel.AddControl(Controls.RuleBuilder().WithTitle("Latency").WithColor(UIConstants.MutedText).Build());
        _latency = Controls.Markup().WithMargin(2, 0, 2, 0).Build();
        panel.AddControl(_latency);

        panel.AddControl(Controls.RuleBuilder().WithTitle("Busiest handlers").WithColor(UIConstants.MutedText).Build());
        _topTable = Controls.Table()
            .AddColumn("Handler", TextJustification.Left, 24)
            .AddColumn("Requests", TextJustification.Right, 14)
            .AddColumn("Share", TextJustification.Left)
            .Rounded().WithBorderColor(UIConstants.MutedText)
            .WithName("overviewTopTable").Build();
        panel.AddControl(_topTable);
    }

    /// <summary>
    /// (Re)build the wrapping card rows: constant-width cards, as many per row as the
    /// content area fits, the rest wrapped onto further rows. Inserts the row grids
    /// at the saved index so the surrounding header/sparkline are preserved.
    /// </summary>
    private void BuildCardRows(int terminalWidth)
    {
        if (_panel is null) return;

        int perRow = CardsPerRow(terminalWidth);
        _lastPerRow = perRow;

        int insertAt = _cardRowStartIndex;
        foreach (var rowCards in Chunk(_cards, perRow))
        {
            var grid = Controls.HorizontalGrid()
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithMargin(0, 0, 0, 1);
            foreach (var card in rowCards)
                grid.Column(col => col.Width(CardWidth).Add(card));
            grid.Column(col => col.Flex(1.0)); // spacer pushes cards left
            var built = grid.Build();
            _panel.InsertControl(insertAt++, built);
            _rowGrids.Add(built);
        }
    }

    /// <summary>
    /// Reflow the cards when the terminal is resized (driven by the driver's
    /// ScreenResized event, mirroring ServerHub). Cheap no-op unless the number of
    /// cards-per-row actually changed. Must run on the UI thread.
    /// </summary>
    public void HandleResize(int terminalWidth)
    {
        if (_panel is null) return;
        if (CardsPerRow(terminalWidth) == _lastPerRow) return; // width changed but layout didn't

        // Remove the stale row grids and rebuild fresh rows in place. The cards
        // themselves are reused (re-parented into the new grids).
        foreach (var grid in _rowGrids)
            _panel.RemoveControl(grid);
        _rowGrids.Clear();

        BuildCardRows(terminalWidth);
    }

    private static int CardsPerRow(int terminalWidth)
    {
        // Content area inside the nav pane (terminal minus sidebar width + borders).
        int contentWidth = Math.Max(CardWidth, terminalWidth - 32);
        return Math.Max(1, contentWidth / CardWidth);
    }

    private static int SafeConsoleWidth()
    {
        try { return Console.WindowWidth > 0 ? Console.WindowWidth : 120; }
        catch { return 120; }
    }

    private static PanelControl Card(string title) =>
        Controls.Panel()
            .WithHeader($" {title} ")
            .HeaderLeft()
            .Rounded()
            .WithBorderColor(UIConstants.MutedText)
            .WithBackgroundColor(UIConstants.ContentBg)
            .WithPadding(1, 0, 1, 0)
            .StretchHorizontal()
            .FillVertical()
            .WordWrap(false)
            .WithContent($"[{UIConstants.MutedText.ToMarkup()}]loading…[/]")
            .Build();

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null) return;

        var muted = UIConstants.MutedText.ToMarkup();
        var accent = UIConstants.Accent.ToMarkup();
        var good = UIConstants.Good.ToMarkup();
        var warn = UIConstants.Warn.ToMarkup();
        var bad = UIConstants.Bad.ToMarkup();
        var s = snap.Status;

        // Each card: a headline status line + supporting detail lines, with ● dots.
        _caddyCard?.SetContent(string.Join('\n', new[]
        {
            "",
            $"{UIConstants.ConnectionDot(s.Running)} {UIConstants.StatusMarkup(s.Running ? "running" : "down")}",
            "",
            $"[{muted}]version[/]  {Escape(s.Version)}",
            $"[{muted}]uptime[/]   {FormatUptime(s.Uptime)}",
        }));

        _sitesCard?.SetContent(string.Join('\n', new[]
        {
            "",
            $"[bold {accent}]{s.RouteCount}[/] [{muted}]routes[/]",
            "",
            $"[{muted}]proxied hosts → upstreams[/]",
        }));

        var certHealth = Services.CertExpiry.Summarize(snap.Certs, snap.Timestamp);
        // Second line: healthy count, or "expiry unknown" when nothing's readable.
        var certHealthy = certHealth.Ok == 0 && certHealth.Unknown > 0
            ? $"[{muted}]●[/] [bold]{certHealth.Unknown}[/] [{muted}]expiry unknown[/]"
            : $"[{good}]●[/] [bold {good}]{certHealth.Ok}[/] [{muted}]healthy[/]";
        // Third line escalates: expired/critical in red when present, else the plain expiring count.
        var certAlert = certHealth.Expired > 0
            ? $"[{bad}]▲[/] [bold {bad}]{certHealth.Expired}[/] [{muted}]expired[/]"
            : certHealth.Critical > 0
                ? $"[{bad}]▲[/] [bold {bad}]{certHealth.Critical}[/] [{muted}]critical (<14d)[/]"
                : $"[{warn}]●[/] [bold {warn}]{certHealth.Warning}[/] [{muted}]expiring (<30d)[/]";
        _certsCard?.SetContent(string.Join('\n', new[]
        {
            "",
            certHealthy,
            certAlert,
            "",
            $"[{muted}]TLS certificate health[/]",
        }));

        _upstreamsCard?.SetContent(string.Join('\n', new[]
        {
            "",
            $"[{good}]●[/] [bold {good}]{s.UpstreamUpCount}[/] [{muted}]up[/]",
            $"[{bad}]●[/] [bold {bad}]{s.UpstreamDownCount}[/] [{muted}]down[/]",
            "",
            $"[{muted}]active reachability probes[/]",
        }));

        _trafficCard?.SetContent(TrafficCardContent(snap.Metrics, muted, good, warn, bad));

        if (_sparkline is not null)
        {
            if (snap.Metrics.Available && snap.Metrics.RequestRate.Count > 0)
            {
                _sparkline.SetDataPoints(snap.Metrics.RequestRate);
                _sparkline.Visible = true;
            }
            else
            {
                // Graceful fallback: hide when /metrics is unavailable.
                _sparkline.Visible = false;
            }
        }

        UpdateMetricsDetail(snap.Metrics, muted);
    }

    // Status-code bars, latency percentiles, and busiest-handlers table (folded in from the
    // former Metrics view). Degrades gracefully when /metrics is off or no traffic has flowed.
    private void UpdateMetricsDetail(Models.MetricsSnapshot m, string muted)
    {
        if (_statusBars is null || _latency is null || _topTable is null) return;
        var sc = m.StatusClasses;
        if (!m.Available || sc.Total <= 0)
        {
            var hint = !m.Available ? "/metrics not enabled" : "no requests recorded yet";
            _statusBars.SetContent(new List<string> { $"[{muted}]{hint}[/]" });
            _latency.SetContent(new List<string> { $"[{muted}]—[/]" });
            _topTable.ClearRows();
            return;
        }

        _statusBars.SetContent(new List<string>
        {
            Bar("2xx", sc.C2xx, sc.Total, UIConstants.Good),
            Bar("3xx", sc.C3xx, sc.Total, UIConstants.AccentBlue),
            Bar("4xx", sc.C4xx, sc.Total, UIConstants.Warn),
            Bar("5xx", sc.C5xx, sc.Total, UIConstants.Bad),
        });

        _latency.SetContent(new List<string>
        {
            m.Latency.Available
                ? $"[{muted}]p50[/] [bold]{FormatMs(m.Latency.P50)}[/]    [{muted}]p95[/] [bold]{FormatMs(m.Latency.P95)}[/]    [{muted}]p99[/] [bold]{FormatMs(m.Latency.P99)}[/]"
                : $"[{muted}]latency histogram unavailable[/]",
        });

        _topTable.ClearRows();
        var handlerTotal = m.TopHandlers.Sum(h => h.Count);
        foreach (var h in m.TopHandlers)
        {
            var share = handlerTotal > 0 ? 100.0 * h.Count / handlerTotal : 0;
            _topTable.AddRow(new TableRow(Escape(h.Label), $"{h.Count:0}", MiniBar(share)));
        }
    }

    private const int BarWidth = 24;
    private static string Bar(string label, double value, double total, SharpConsoleUI.Color color)
    {
        var pct = total > 0 ? value / total : 0;
        int filled = Math.Clamp((int)Math.Round(pct * BarWidth), 0, BarWidth);
        var c = color.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        var bar = $"[{c}]{new string('█', filled)}[/][{muted}]{new string('░', BarWidth - filled)}[/]";
        return $"[{muted}]{label}[/]  {bar}  [bold]{pct * 100:0}%[/] [{muted}]({value:0})[/]";
    }

    private static string MiniBar(double sharePct)
    {
        int filled = Math.Clamp((int)Math.Round(sharePct / 100.0 * 16), 0, 16);
        var c = UIConstants.AccentBlue.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        return $"[{c}]{new string('█', filled)}[/][{muted}]{new string('░', 16 - filled)}[/] {sharePct:0}%";
    }

    // The Traffic card: current rate, in-flight, p95 latency, and a 2xx/4xx/5xx split.
    // caddy_http_requests_total is often absent until traffic flows; show a hint then.
    private static string TrafficCardContent(Models.MetricsSnapshot m, string muted, string good, string warn, string bad)
    {
        if (!m.Available)
            return "\n" + $"[{muted}]/metrics not enabled[/]";

        var rate = m.RequestRate.Count > 0 ? m.RequestRate[^1] : 0d;
        var sc = m.StatusClasses;
        if (sc.Total <= 0)
            return string.Join('\n', new[]
            {
                "",
                $"[bold {good}]{rate:0.0}[/] [{muted}]req/s[/]",
                "",
                $"[{muted}]no requests yet[/]",
            });

        var p95 = m.Latency.Available ? FormatMs(m.Latency.P95) : "—";
        double pct(double v) => sc.Total > 0 ? 100.0 * v / sc.Total : 0;
        return string.Join('\n', new[]
        {
            "",
            $"[bold {good}]{rate:0.0}[/] [{muted}]req/s[/]   [{muted}]in-flight[/] {m.InFlight:0}",
            $"[{muted}]p95[/] {p95}",
            "",
            $"[{good}]{pct(sc.C2xx):0}%[/] [{muted}]2xx[/]  [{warn}]{pct(sc.C4xx):0}%[/] [{muted}]4xx[/]  [{bad}]{pct(sc.C5xx):0}%[/] [{muted}]5xx[/]",
        });
    }

    // Seconds → a compact human latency string (µs/ms/s).
    internal static string FormatMs(double seconds)
    {
        if (seconds <= 0) return "0ms";
        var ms = seconds * 1000.0;
        if (ms < 1) return $"{seconds * 1_000_000:0}µs";
        if (ms < 100) return $"{ms:0.0}ms";
        if (ms < 1000) return $"{ms:0}ms";
        return $"{seconds:0.00}s";
    }

    private static IEnumerable<T[]> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        for (int i = 0; i < items.Count; i += size)
            yield return items.Skip(i).Take(size).ToArray();
    }

    private static string FormatUptime(TimeSpan up)
    {
        if (up.TotalDays >= 1) return $"{(int)up.TotalDays}d {up.Hours}h";
        if (up.TotalHours >= 1) return $"{(int)up.TotalHours}h {up.Minutes}m";
        return $"{up.Minutes}m";
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
