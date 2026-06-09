// -----------------------------------------------------------------------
// LazyCaddy - topology view. Builds the graph from the latest snapshot, lays
// it out, and renders it onto a CanvasControl sized to the FULL graph extent.
// The canvas lives inside a ScrollablePanelControl, so panning (arrows / PgUp/
// PgDn / Home / End / scrollbars / mouse wheel) is handled by the framework —
// the renderer always draws at absolute coordinates (no manual scroll offset).
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Topology;

namespace LazyCaddy.Views;

public sealed class TopologyView
{
    private const int BoxW = 22, BoxH = 3, HGap = 6, VGap = 1;
    private const int Pad = 2; // breathing room around the graph in the canvas buffer

    private CanvasControl? _canvas;
    private ScrollablePanelControl? _scroller;   // focused on view entry so arrows pan the graph
    private IReadOnlyList<PlacedNode> _placed = System.Array.Empty<PlacedNode>();
    private IReadOnlyList<TopoEdge> _edges = System.Array.Empty<TopoEdge>();

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Topology[/]")
            .AddLine($"[{muted}]host → handler chain → upstream. Arrows / PgUp·PgDn / Home·End scroll. Health-colored.[/]")
            .AddEmptyLine().Build());

        // Explicitly-sized canvas (AutoSize OFF): its size is set each Update to the full graph
        // extent, so it can be larger than the viewport. Wrapping it in a ScrollablePanel with both
        // scroll modes lets the framework pan/clip — long handler chains and the rightmost upstream
        // column are reachable by scrolling instead of overrunning the border.
        // Disabled = not focusable. The canvas is a pure rendered diagram (no interaction), so it
        // must not grab key focus — otherwise the ScrollablePanel would delegate arrow/PgUp/End
        // keys to the canvas instead of scrolling. With it non-focusable, focus rests on the
        // scroller, which handles all scrolling. (Disabling doesn't affect the canvas paint.)
        _canvas = Controls.Canvas()
            .AutoSize(false)
            .WithSize(40, 10) // placeholder; resized on first Update from the layout extent
            .Enabled(false)
            .WithName("topologyCanvas")
            .Build();

        _canvas.Paint += (_, e) =>
            TopologyRenderer.Render(e.Graphics, e.CanvasWidth, e.CanvasHeight,
                _placed, _edges, selectedId: null, scrollX: 0, scrollY: 0);

        var scroller = Controls.ScrollablePanel()
            .WithHorizontalScroll(ScrollMode.Scroll)
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithName("topologyScroller")
            .AddControl(_canvas)
            .Build();

        _scroller = scroller;
        panel.AddControl(scroller);
    }

    /// <summary>Focus the scroller so arrows / PgUp·PgDn / Home·End pan the graph on view entry.</summary>
    public void FocusPrimary() => _scroller?.RequestFocus();

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null || _canvas is null) return;

        var graph = TopologyGraph.Build(snap);
        _placed = LayeredLayout.Arrange(graph, BoxW, BoxH, HGap, VGap);
        _edges = graph.Edges;

        // Size the canvas buffer to the full graph so nothing is clipped at the source; the
        // ScrollablePanel provides the viewport and scrolling.
        var (w, h) = Extent(_placed);
        _canvas.CanvasWidth = w;
        _canvas.CanvasHeight = h;
        _canvas.Invalidate(); // request repaint on the UI thread
    }

    /// <summary>Redraw on terminal resize (driver ScreenResized). Call on the UI thread.</summary>
    public void HandleResize() => _canvas?.Invalidate();

    // The buffer size needed to hold every node: max right/bottom edge, plus a little padding.
    private static (int W, int H) Extent(IReadOnlyList<PlacedNode> placed)
    {
        int maxX = 0, maxY = 0;
        foreach (var p in placed)
        {
            if (p.X + p.Width > maxX) maxX = p.X + p.Width;
            if (p.Y + p.Height > maxY) maxY = p.Y + p.Height;
        }
        return (System.Math.Max(1, maxX + Pad), System.Math.Max(1, maxY + Pad));
    }
}
