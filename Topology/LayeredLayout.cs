// -----------------------------------------------------------------------
// LazyCaddy - deterministic placement of a topology graph into a grid.
// COLUMN (X) = a node's position in its route's handler chain (Host = 0,
// each successive handler the next column, upstream one past its proxy).
// ROW (Y) = swim-lane = the route the node belongs to, so each route's whole
// chain stays on one row and edges never cross between routes.
// -----------------------------------------------------------------------

namespace LazyCaddy.Topology;

public sealed record PlacedNode(TopoNode Node, int X, int Y, int Width, int Height);

public static class LayeredLayout
{
    /// <param name="boxWidth">
    /// MINIMUM box width. Each column is widened past this to fit its longest label, so nodes
    /// are never truncated and columns of short labels stay narrow.
    /// </param>
    public static IReadOnlyList<PlacedNode> Arrange(
        TopologyGraph graph, int boxWidth, int boxHeight, int hGap, int vGap)
    {
        var placed = new List<PlacedNode>();

        // COLUMN (X) = the node's chain position, compacted: if no route is long enough to use a
        // given column it consumes no horizontal space (so a fleet of short chains stays tight).
        var usedCols = graph.Nodes.Select(n => n.Column).Distinct().OrderBy(c => c).ToList();

        // Each column is sized to its widest label rather than one global width, so a long host
        // like "aperitto.duckdns.org :8444" is not truncated while narrow upstream columns stay
        // tight. boxWidth acts as the MINIMUM. +2 for the box's left/right border characters.
        var colWidth = usedCols.ToDictionary(
            c => c,
            c => Math.Max(
                boxWidth,
                graph.Nodes.Where(n => n.Column == c)
                           .Select(n => (n.Label?.Length ?? 0) + 2)
                           .DefaultIfEmpty(boxWidth)
                           .Max()));

        // Running x offset per column: sum of every preceding column's width plus one gap each.
        var colX = new Dictionary<int, int>(usedCols.Count);
        int xCursor = 0;
        foreach (var c in usedCols)
        {
            colX[c] = xCursor;
            xCursor += colWidth[c] + hGap;
        }

        // ROW (Y) = swim-lane = the route the node belongs to. Lanes are compacted to consecutive
        // rows. Shared/laneless upstreams (Lane = -1) stack in their own rows from the top, since
        // one upstream can be fed by proxies on several lanes.
        var laneRow = graph.Nodes.Where(n => n.Lane >= 0).Select(n => n.Lane)
            .Distinct().OrderBy(l => l)
            .Select((lane, idx) => (lane, idx)).ToDictionary(t => t.lane, t => t.idx);

        int Step(int n) => n * (boxHeight + vGap);

        // Laned nodes: X from chain column, Y from the node's lane.
        foreach (var node in graph.Nodes.Where(n => n.Lane >= 0))
        {
            int x = colX[node.Column];
            int y = Step(laneRow[node.Lane]);
            placed.Add(new PlacedNode(node, x, y, colWidth[node.Column], boxHeight));
        }

        // Laneless upstream nodes: X from their (shared) column, stacked from the top.
        int upRow = 0;
        foreach (var node in graph.Nodes.Where(n => n.Lane < 0))
        {
            int x = colX[node.Column];
            int y = Step(upRow++);
            placed.Add(new PlacedNode(node, x, y, colWidth[node.Column], boxHeight));
        }

        return placed;
    }
}
