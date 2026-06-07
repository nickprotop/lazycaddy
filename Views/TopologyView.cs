// -----------------------------------------------------------------------
// LazyCaddy - interactive topology view. Owns a CanvasControl, builds the
// graph from the latest snapshot, lays it out, and renders. Arrow keys move
// the selection between nodes; the canvas redraws on poll and on input.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Topology;

namespace LazyCaddy.Views;

public sealed class TopologyView
{
    private const int BoxW = 22, BoxH = 3, HGap = 6, VGap = 1;

    private CanvasControl? _canvas;
    private IReadOnlyList<PlacedNode> _placed = System.Array.Empty<PlacedNode>();
    private IReadOnlyList<TopoEdge> _edges = System.Array.Empty<TopoEdge>();
    private string? _selectedId;
    // _scrollX is reserved for future horizontal panning; arrows currently drive
    // selection (left/right) and vertical scroll (up/down). Initialized explicitly
    // so it is treated as assigned (no CS0649) until horizontal panning lands.
    private int _scrollX = 0, _scrollY;

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Topology[/]")
            .AddLine($"[{muted}]host → reverse_proxy → upstream. Arrows: select/scroll. Health-colored.[/]")
            .AddEmptyLine().Build());

        // AutoSize makes CanvasWidth/Height track the available content area, so the
        // whole graph (incl. the rightmost upstream column) is in bounds AND the canvas
        // re-sizes when the terminal resizes. Stretch horizontally to fill the panel.
        _canvas = Controls.Canvas()
            .AutoSize(true)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithName("topologyCanvas")
            .Build();

        _canvas.Paint += (_, e) =>
            TopologyRenderer.Render(e.Graphics, e.CanvasWidth, e.CanvasHeight,
                _placed, _edges, _selectedId, _scrollX, _scrollY);

        _canvas.CanvasKeyPressed += (_, key) => OnKey(key);
        panel.AddControl(_canvas);
    }

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null || _canvas is null) return;

        var graph = TopologyGraph.Build(snap);
        _placed = LayeredLayout.Arrange(graph, BoxW, BoxH, HGap, VGap);
        _edges = graph.Edges;
        _selectedId ??= _placed.FirstOrDefault()?.Node.Id;
        _canvas.Invalidate(); // request repaint on the UI thread
    }

    /// <summary>Redraw on terminal resize (driver ScreenResized). Call on the UI thread.</summary>
    public void HandleResize() => _canvas?.Invalidate();

    private void OnKey(System.ConsoleKeyInfo key)
    {
        if (_placed.Count == 0) return;
        var ordered = _placed.OrderBy(p => p.X).ThenBy(p => p.Y).Select(p => p.Node.Id).ToList();
        int idx = _selectedId is null ? 0 : System.Math.Max(0, ordered.IndexOf(_selectedId));

        switch (key.Key)
        {
            case System.ConsoleKey.RightArrow: idx = System.Math.Min(ordered.Count - 1, idx + 1); break;
            case System.ConsoleKey.LeftArrow:  idx = System.Math.Max(0, idx - 1); break;
            case System.ConsoleKey.DownArrow:  _scrollY += 1; break;
            case System.ConsoleKey.UpArrow:    _scrollY = System.Math.Max(0, _scrollY - 1); break;
            default: return;
        }
        _selectedId = ordered[idx];
        _canvas?.Invalidate();
    }
}
