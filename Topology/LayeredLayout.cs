// -----------------------------------------------------------------------
// LazyCaddy - deterministic left-to-right layered placement of a topology
// graph. Layer index = node kind order; row = order within the layer.
// -----------------------------------------------------------------------

namespace LazyCaddy.Topology;

public sealed record PlacedNode(TopoNode Node, int X, int Y, int Width, int Height);

public static class LayeredLayout
{
    private static int Layer(NodeKind k) => k switch
    {
        NodeKind.Host => 0,
        NodeKind.Matcher => 1,
        NodeKind.Middleware => 2,
        NodeKind.ReverseProxy => 3,
        NodeKind.Terminal => 3,   // a terminal handler ends the chain in the same column as a proxy
        NodeKind.Upstream => 4,
        _ => 5
    };

    public static IReadOnlyList<PlacedNode> Arrange(
        TopologyGraph graph, int boxWidth, int boxHeight, int hGap, int vGap)
    {
        var placed = new List<PlacedNode>();
        // Order layers by kind, but COMPACT them: empty layers (e.g. no matcher /
        // middleware nodes) consume no column, so host → reverse_proxy → upstream sit
        // in adjacent columns instead of leaving dead horizontal gaps.
        var byLayer = graph.Nodes
            .GroupBy(n => Layer(n.Kind))
            .OrderBy(g => g.Key)
            .ToList();

        for (int col = 0; col < byLayer.Count; col++)
        {
            int x = col * (boxWidth + hGap);
            int row = 0;
            foreach (var node in byLayer[col])
            {
                int y = row * (boxHeight + vGap);
                placed.Add(new PlacedNode(node, x, y, boxWidth, boxHeight));
                row++;
            }
        }
        return placed;
    }
}
