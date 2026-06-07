// -----------------------------------------------------------------------
// LazyCaddy - draws a laid-out topology graph onto a CanvasGraphics surface.
// Health-colored boxes + connecting lines. Highlights the selected node.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Drawing;
using LazyCaddy.Configuration;

namespace LazyCaddy.Topology;

public static class TopologyRenderer
{
    public static void Render(
        CanvasGraphics g, int canvasW, int canvasH,
        IReadOnlyList<PlacedNode> nodes, IReadOnlyList<TopoEdge> edges,
        string? selectedId, int scrollX, int scrollY)
    {
        g.Clear(UIConstants.ContentBg);

        var pos = nodes.ToDictionary(n => n.Node.Id, n => n);

        // Edges first (so boxes draw on top).
        foreach (var e in edges)
        {
            if (!pos.TryGetValue(e.FromId, out var a) || !pos.TryGetValue(e.ToId, out var b)) continue;
            int x0 = a.X + a.Width - scrollX;
            int y0 = a.Y + a.Height / 2 - scrollY;
            int x1 = b.X - scrollX;
            int y1 = b.Y + b.Height / 2 - scrollY;
            g.DrawLine(x0, y0, x1, y1, '─', UIConstants.MutedText, UIConstants.ContentBg);
        }

        foreach (var p in nodes)
        {
            int x = p.X - scrollX, y = p.Y - scrollY;
            if (x + p.Width < 0 || y + p.Height < 0 || x >= canvasW || y >= canvasH) continue; // cull

            var color = p.Node.Health switch
            {
                NodeHealth.Up => UIConstants.Good,
                NodeHealth.Down => UIConstants.Bad,
                NodeHealth.Warn => UIConstants.Warn,
                _ => UIConstants.MutedText
            };
            bool sel = p.Node.Id == selectedId;
            var border = sel ? UIConstants.Accent : color;

            g.DrawBox(x, y, p.Width, p.Height, BoxChars.Rounded, border, UIConstants.ContentBg);
            g.WriteString(x + 1, y + 1, Truncate(p.Node.Label, p.Width - 2), color, UIConstants.ContentBg);
        }
    }

    private static string Truncate(string s, int max)
        => max <= 0 ? "" : (s.Length <= max ? s : (max <= 1 ? s[..max] : s[..(max - 1)] + "…"));
}
