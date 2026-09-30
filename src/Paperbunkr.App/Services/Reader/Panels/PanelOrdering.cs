using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services.Reader.Panels;

/// <summary>
/// Puts a bag of panel rectangles into reading order (docs/superpowers/specs/2026-09-28-guided-view-detection-upgrade-design.md step 3): rows top to bottom, each row left to right
/// (right to left for manga). A panel joins the row of the highest remaining panel when it overlaps that panel vertically by at least half of the shorter one; inside a row, panels
/// whose left edges are within <see cref="SameColumnTolerance"/> read top to bottom (a stack beside a tall panel). Shared by every detector.
/// </summary>
public static class PanelOrdering
{
    /// <summary>Left edges closer than this fraction of the page width count as the same column.</summary>
    public const double SameColumnTolerance = 0.04;

    /// <summary>Minimum vertical overlap, as a fraction of the shorter panel's height, for two panels to share a row.</summary>
    public const double RowOverlap = 0.5;

    public static IReadOnlyList<PanelRect> Sort(IEnumerable<PanelRect> panels, bool rightToLeft) =>
        Rows(panels).SelectMany(row => SortRow(row, rightToLeft)).ToList();

    /// <summary>Groups panels into rows, top to bottom (panels inside a row are in no particular order).</summary>
    public static List<List<PanelRect>> Rows(IEnumerable<PanelRect> panels)
    {
        var remaining = panels.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        var rows = new List<List<PanelRect>>();
        while (remaining.Count > 0)
        {
            var seed = remaining[0];
            var row = remaining.Where(p => p.Equals(seed) || SharesRow(seed, p)).ToList();
            remaining.RemoveAll(row.Contains);
            rows.Add(row);
        }

        return rows;
    }

    private static bool SharesRow(PanelRect a, PanelRect b)
    {
        double overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return overlap >= RowOverlap * Math.Min(a.Height, b.Height);
    }

    private static IEnumerable<PanelRect> SortRow(List<PanelRect> row, bool rightToLeft)
    {
        // Group into columns by left edge (RTL: by right edge), then order the columns and each column's panels top to bottom.
        var columns = new List<List<PanelRect>>();
        foreach (var p in row.OrderBy(p => rightToLeft ? -p.Right : p.X))
        {
            var column = columns.FirstOrDefault(c => Math.Abs(Edge(c[0], rightToLeft) - Edge(p, rightToLeft)) <= SameColumnTolerance);
            if (column is null)
            {
                columns.Add([p]);
            }
            else
            {
                column.Add(p);
            }
        }

        return columns.SelectMany(c => c.OrderBy(p => p.Y));
    }

    private static double Edge(PanelRect p, bool rightToLeft) => rightToLeft ? p.Right : p.X;
}
