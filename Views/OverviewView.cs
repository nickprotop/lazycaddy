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
    private SparklineControl? _sparkline;

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
        _cards = new[] { _caddyCard, _sitesCard, _certsCard, _upstreamsCard };

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

        _certsCard?.SetContent(string.Join('\n', new[]
        {
            "",
            $"[{good}]●[/] [bold {good}]{s.CertValidCount}[/] [{muted}]valid[/]",
            $"[{warn}]●[/] [bold {warn}]{s.CertExpiringCount}[/] [{muted}]expiring (<30d)[/]",
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
