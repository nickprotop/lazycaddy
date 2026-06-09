// -----------------------------------------------------------------------
// LazyCaddy - draws a laid-out topology graph onto a CanvasGraphics surface.
// Health-colored boxes + connecting lines. Highlights the selected node.
// -----------------------------------------------------------------------

using System;
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

        // Edges first (so boxes draw on top). Drawn as clean orthogonal "elbow" connectors
        // (horizontal → vertical → horizontal) using box-drawing glyphs, instead of a single
        // diagonal char that renders as a ragged staircase.
        foreach (var e in edges)
        {
            if (!pos.TryGetValue(e.FromId, out var a) || !pos.TryGetValue(e.ToId, out var b)) continue;
            int x0 = a.X + a.Width - scrollX;
            int y0 = a.Y + a.Height / 2 - scrollY;
            int x1 = b.X - scrollX;
            int y1 = b.Y + b.Height / 2 - scrollY;
            DrawConnector(g, x0, y0, x1, y1, canvasW, canvasH);
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

            // Clear the box interior first: DrawBox only strokes the border, so any connector line
            // routed underneath would otherwise show through the box (a '─' across the center row).
            int innerW = Math.Max(0, Math.Min(p.Width - 2, canvasW - (x + 1)));
            if (innerW > 0)
            {
                var blank = new string(' ', innerW);
                for (int row = 1; row < p.Height - 1; row++)
                {
                    int yy = y + row;
                    if (x + 1 >= 0 && yy >= 0 && yy < canvasH)
                        g.WriteString(x + 1, yy, blank, UIConstants.ContentBg, UIConstants.ContentBg);
                }
            }

            g.DrawBox(x, y, p.Width, p.Height, BoxChars.Rounded, border, UIConstants.ContentBg);
            g.WriteString(x + 1, y + 1, Truncate(p.Node.Label, p.Width - 2), color, UIConstants.ContentBg);
        }
    }

    private static string Truncate(string s, int max)
        => max <= 0 ? "" : (s.Length <= max ? s : (max <= 1 ? s[..max] : s[..(max - 1)] + "…"));

    // Draw a clean orthogonal connector from (x0,y0) on the source's right edge to (x1,y1) on the
    // target's left edge. Same row → a single horizontal run. Different rows → run right to a mid
    // column, turn, run vertically to the target row, turn, run into the target — with proper
    // corner glyphs at the two bends. All cells clipped to the canvas so nothing crosses the border.
    private static void DrawConnector(CanvasGraphics g, int x0, int y0, int x1, int y1, int w, int h)
    {
        var fg = UIConstants.MutedText;
        if (x1 <= x0) { Plot(g, x0, y0, '─', fg, w, h); return; } // degenerate; just mark the source side

        if (y0 == y1)
        {
            for (int x = x0; x < x1; x++) Plot(g, x, y0, '─', fg, w, h);
            return;
        }

        // Bend at the midpoint column between the two boxes.
        int xm = x0 + Math.Max(1, (x1 - x0) / 2);
        bool down = y1 > y0;

        // 1) horizontal from source to the bend column
        for (int x = x0; x < xm; x++) Plot(g, x, y0, '─', fg, w, h);
        // 2) first corner: leaving a horizontal run and turning vertical
        Plot(g, xm, y0, down ? '╮' : '╯', fg, w, h);
        // 3) vertical run between the rows (exclusive of the corner rows)
        int yStep = down ? 1 : -1;
        for (int y = y0 + yStep; y != y1; y += yStep) Plot(g, xm, y, '│', fg, w, h);
        // 4) second corner: turning back to horizontal toward the target
        Plot(g, xm, y1, down ? '╰' : '╭', fg, w, h);
        // 5) horizontal from the bend column into the target
        for (int x = xm + 1; x < x1; x++) Plot(g, x, y1, '─', fg, w, h);
    }

    // Write a single connector cell, clipped to the canvas bounds.
    private static void Plot(CanvasGraphics g, int x, int y, char ch, Color fg, int w, int h)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        g.WriteString(x, y, ch.ToString(), fg, UIConstants.ContentBg);
    }
}
