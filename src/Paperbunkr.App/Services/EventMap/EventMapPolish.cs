using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>
/// The "read so far" line (docs/superpowers/specs/2026-11-11-continuity-map-polish-design.md §1). Every edge kind follows one rule: an edge
/// is read when its <b>target</b> card is read (relay chains up to the last read issue, spine sequences per lane, a tie-in once the lane's
/// first card in the segment is read, a promoted prologue/epilogue's continuity edge once its next issue is read). In progress counts as
/// unread.
/// </summary>
public static class EventMapReadPath
{
    public static bool[] Classify(EventMapLayoutResult layout, Func<int, bool> isRead) =>
        layout.Edges.Select(e => e.To >= 0 && e.To < layout.Cells.Count && isRead(e.To)).ToArray();

    /// <summary>The "You are here" card: the first unread trunk card in spine mode, else the first unread card; null when all is read.</summary>
    public static int? NextUp(EventMapLayoutResult layout, Func<int, bool> isRead)
    {
        var ordered = layout.Cells.Where(c => !c.Row.IsGhost).OrderBy(c => c.Index);
        var first = layout.IsSpineMode
            ? ordered.FirstOrDefault(c => c.IsTrunk && !isRead(c.Index)) ?? ordered.FirstOrDefault(c => !isRead(c.Index))
            : ordered.FirstOrDefault(c => !isRead(c.Index));
        return first?.Index;
    }
}

/// <summary>
/// Trace on select (§3): hop distances from the selected card over the edges inside the related set, and when each hop starts.
/// </summary>
public static class EventMapTrace
{
    public const double StaggerMs = 60;

    /// <summary>Hop number per related cell (the selected card is hop 0); cells not reachable through related edges get the last hop + 1.</summary>
    public static Dictionary<int, int> Hops(EventMapLayoutResult layout, int selected, IReadOnlySet<int> related)
    {
        var hops = new Dictionary<int, int> { [selected] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(selected);
        var neighbours = new Dictionary<int, List<int>>();
        foreach (var edge in layout.Edges)
        {
            if (!related.Contains(edge.From) || !related.Contains(edge.To))
            {
                continue;
            }

            (neighbours.TryGetValue(edge.From, out var a) ? a : neighbours[edge.From] = new List<int>()).Add(edge.To);
            (neighbours.TryGetValue(edge.To, out var b) ? b : neighbours[edge.To] = new List<int>()).Add(edge.From);
        }

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            if (!neighbours.TryGetValue(cell, out var next))
            {
                continue;
            }

            foreach (int n in next)
            {
                if (hops.TryAdd(n, hops[cell] + 1))
                {
                    queue.Enqueue(n);
                }
            }
        }

        int last = hops.Values.Max();
        foreach (int cell in related)
        {
            hops.TryAdd(cell, last + 1);
        }

        return hops;
    }

    /// <summary>The stagger between hops: 60 ms, compressed so the whole trace fits in <paramref name="totalMs"/>.</summary>
    public static double Stagger(int hopCount, double totalMs) =>
        hopCount <= 1 ? 0 : Math.Min(StaggerMs, totalMs / (hopCount + 1));

    /// <summary>
    /// The first <paramref name="fraction"/> of a polyline (by length), for drawing an edge "in" from its start. 0 gives the start point
    /// alone; 1 gives the whole line.
    /// </summary>
    public static IReadOnlyList<Point> Trim(IReadOnlyList<Point> points, double fraction)
    {
        if (points.Count < 2 || fraction >= 1)
        {
            return points;
        }

        fraction = Math.Max(0, fraction);
        double total = 0;
        for (int i = 1; i < points.Count; i++)
        {
            total += Distance(points[i - 1], points[i]);
        }

        double remaining = total * fraction;
        var result = new List<Point> { points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            double segment = Distance(points[i - 1], points[i]);
            if (remaining >= segment)
            {
                result.Add(points[i]);
                remaining -= segment;
                continue;
            }

            double t = segment == 0 ? 0 : remaining / segment;
            result.Add(points[i - 1] + ((points[i] - points[i - 1]) * t));
            break;
        }

        return result;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
}

/// <summary>Save as image (§4): which card look (scale) to export at, stepping down until the image fits.</summary>
public static class EventMapExportScale
{
    public const double MaxPixels = 16000;

    /// <summary>Covers are decoded at full size for an export, so a big map steps down from Covers to keep memory bounded.</summary>
    public const int MaxCoverCards = 300;

    /// <summary>The scale to export at, or null when even Dots would exceed <see cref="MaxPixels"/> (the render is refused).</summary>
    public static double? Choose(EventMapLayoutResult layout, double currentScale, double chromeWidth, double chromeHeight)
    {
        var looks = new[] { EventMapCardLook.Covers, EventMapCardLook.Standard, EventMapCardLook.Compact, EventMapCardLook.Chips, EventMapCardLook.Dots };
        var current = EventMapCardLooks.For(EventMapMetrics.ForScale(currentScale).CardWidth);
        foreach (var look in looks.SkipWhile(l => l > current))
        {
            if (look == EventMapCardLook.Covers && layout.Cells.Count > MaxCoverCards)
            {
                continue;
            }

            double scale = look == current ? currentScale : EventMapZoom.ScaleForLook(look);
            var size = layout.TotalSizeFor(EventMapMetrics.ForScale(scale));
            if (size.Width + chromeWidth <= MaxPixels && size.Height + chromeHeight <= MaxPixels)
            {
                return scale;
            }
        }

        return null;
    }
}
