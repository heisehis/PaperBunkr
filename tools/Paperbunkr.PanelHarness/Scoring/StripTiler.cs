using Paperbunkr.App.Services.Reader.Panels;
using SkiaSharp;

namespace Paperbunkr.PanelHarness.Scoring;

/// <summary>
/// Runs the panel model on a tall webtoon strip by cutting it into overlapping tiles (docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md,
/// Webtoon tiler and Scorer). Scoring only: the app still sends strips to <see cref="PanelDetector.DetectStrip"/>. Boxes cut by a tile seam, or seen
/// twice in an overlap, are merged back into one.
/// </summary>
internal static class StripTiler
{
    /// <summary>Tile height as a multiple of the strip width.</summary>
    public const double TileAspect = 1.5;

    /// <summary>How much consecutive tiles overlap, as a fraction of the tile height.</summary>
    public const double Overlap = 0.25;

    /// <summary>Two boxes merge when they overlap vertically and their horizontal overlap is at least this share of the narrower one.</summary>
    public const double MergeColumnShare = 0.5;

    /// <summary>The tiles (top and height in pixels) for a strip of the given size. The last tile ends at the bottom of the strip.</summary>
    public static List<(int Top, int Height)> Tiles(int width, int height)
    {
        int tileH = Math.Max(1, (int)Math.Round(width * TileAspect));
        if (tileH >= height)
        {
            return [(0, height)];
        }

        int step = Math.Max(1, (int)Math.Round(tileH * (1 - Overlap)));
        var tiles = new List<(int, int)>();
        for (int top = 0; ; top += step)
        {
            if (top + tileH >= height)
            {
                tiles.Add((height - tileH, tileH));
                break;
            }

            tiles.Add((top, tileH));
        }

        return tiles;
    }

    public static PagePanels Detect(OnnxPanelDetector detector, SKBitmap strip, bool rightToLeft)
    {
        var boxes = new List<PanelRect>();
        foreach (var (top, tileH) in Tiles(strip.Width, strip.Height))
        {
            using var tile = new SKBitmap();
            if (!strip.ExtractSubset(tile, new SKRectI(0, top, strip.Width, top + tileH)))
            {
                continue;
            }

            foreach (var r in detector.Run(tile))
            {
                boxes.Add(new PanelRect(r.X, ((r.Y * tileH) + top) / strip.Height, r.Width, (r.Height * tileH) / strip.Height));
            }
        }

        var merged = Merge(boxes);
        return merged.Count == 0 ? PagePanels.Whole : new PagePanels(PanelOrdering.Sort(merged, rightToLeft), true);
    }

    /// <summary>Repeatedly replaces any two boxes that overlap vertically and share a column with their union.</summary>
    public static List<PanelRect> Merge(IEnumerable<PanelRect> input)
    {
        var boxes = input.ToList();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < boxes.Count && !changed; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (SameColumn(boxes[i], boxes[j]) && Math.Min(boxes[i].Bottom, boxes[j].Bottom) > Math.Max(boxes[i].Y, boxes[j].Y))
                    {
                        var a = boxes[i];
                        var b = boxes[j];
                        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
                        boxes[i] = new PanelRect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
                        boxes.RemoveAt(j);
                        changed = true;
                        break;
                    }
                }
            }
        }

        return boxes;
    }

    private static bool SameColumn(PanelRect a, PanelRect b)
    {
        double overlap = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        return overlap > 0 && overlap >= MergeColumnShare * Math.Min(a.Width, b.Width);
    }
}
