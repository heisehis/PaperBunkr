using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services.Reader.Panels;

/// <summary>A rectangle on a page as fractions of the page's width and height (0-1, origin top-left).</summary>
public readonly record struct PanelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    public double Area => Width * Height;

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    /// <summary>The whole page.</summary>
    public static PanelRect WholePage => new(0, 0, 1, 1);
}

/// <summary>The panels found on one page, in reading order. <see cref="Confident"/> is false when detection gave up and the single rectangle is just the whole page.</summary>
public sealed record PagePanels(IReadOnlyList<PanelRect> Rects, bool Confident)
{
    public static PagePanels Whole { get; } = new([PanelRect.WholePage], false);

    public int Count => Rects.Count;
}

/// <summary>
/// Finds the panels of a comic page from its luminance (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md section 2). A local, dependency-free heuristic: the page is treated as a
/// tree of cuts along "gutters", rows or columns that are almost entirely background (white-ish or black-ish, whichever the page uses), and each leaf of the tree, trimmed to its content, is a panel.
/// When nothing convincing is found (splash pages, borderless art, textured pages) it returns the whole page as one unconfident panel, so guided view can simply step past it.
/// <para>
/// Pure and deterministic: it takes a byte grid and returns fractions, so it is tested with synthetic pages. Every threshold is a named constant, tuned on real pages with the tuning overlay.
/// </para>
/// </summary>
public static class PanelDetector
{
    /// <summary>The long side the detector itself analyses pages at. Higher than <see cref="AnalysisSize"/> (still used by auto-levels/crop) because real gutters are only a few pixels wide on a 480 px page.</summary>
    public const int DetectionSize = 1000;

    /// <summary>The long side pages are analysed at (callers downscale to about this).</summary>
    public const int AnalysisSize = 480;

    /// <summary>A pixel at or above this luminance is "white-ish" background.</summary>
    public const int LightBackgroundLuma = 235;

    /// <summary>A pixel at or below this luminance is "black-ish" background.</summary>
    public const int DarkBackgroundLuma = 20;

    /// <summary>A row or column is gutter when at least this fraction of it is background.</summary>
    public const double GutterFraction = 0.985;

    /// <summary>The thinnest gutter that separates two panels, as a fraction of the page dimension it cuts across (with a 3 px floor).</summary>
    public const double MinGutter = 0.006;

    /// <summary>Panels smaller than this fraction of the page are noise and are dropped.</summary>
    public const double MinPanelArea = 0.04;

    /// <summary>Panels narrower or shorter than this fraction of the page are noise and are dropped.</summary>
    public const double MinPanelSide = 0.05;

    /// <summary>Below this share of the page covered by the panels, detection is not trusted (mostly noise or art without gutters).</summary>
    public const double MinCoverage = 0.30;

    private const int MaxDepth = 8;
    private const int MaxPanels = 24;

    /// <summary>
    /// Detects the panels of a page. <paramref name="luma"/> is <paramref name="width"/> x <paramref name="height"/> luminance bytes, row-major. <paramref name="rightToLeft"/> reverses the order of
    /// panels side by side (manga). Never throws for a degenerate input: it returns <see cref="PagePanels.Whole"/>.
    /// </summary>
    public static PagePanels Detect(byte[] luma, int width, int height, bool rightToLeft)
    {
        if (luma is null || width < 16 || height < 16 || luma.Length < width * height)
        {
            return PagePanels.Whole;
        }

        // The page's own polarity first (white gutters or black ones, judged from its border); the other polarity only if that finds nothing.
        bool lightFirst = BorderIsLight(luma, width, height);
        var first = DetectWith(luma, width, height, rightToLeft, light: lightFirst);
        if (first.Confident)
        {
            return first;
        }

        var second = DetectWith(luma, width, height, rightToLeft, light: !lightFirst);
        return second.Confident ? second : PagePanels.Whole;
    }

    /// <summary>The narrowest run of empty rows that separates two blocks of a scrolling strip, as a fraction of the strip width.</summary>
    public const double StripMinGutter = 0.04;

    /// <summary>Blocks of a strip shorter than this fraction of its width are stray text or effects and are dropped.</summary>
    public const double StripMinBlock = 0.12;

    private const int MaxStripBlocks = 400;

    /// <summary>
    /// Detects the blocks of a tall scrolling strip (webtoon/manhwa) by its full-width empty bands: everything between two such bands is one block, trimmed sideways to its content. Strips have no
    /// side-by-side panels, so this only cuts rows. Returns <see cref="PagePanels.Whole"/> when the strip has fewer than two blocks (no usable bands, for example a patterned background).
    /// </summary>
    public static PagePanels DetectStrip(byte[] luma, int width, int height)
    {
        if (luma is null || width < 16 || height < 16 || luma.Length < width * height)
        {
            return PagePanels.Whole;
        }

        bool lightFirst = BorderIsLight(luma, width, height);
        var first = DetectStripWith(luma, width, height, lightFirst);
        if (first.Confident)
        {
            return first;
        }

        var second = DetectStripWith(luma, width, height, !lightFirst);
        return second.Confident ? second : PagePanels.Whole;
    }

    private static PagePanels DetectStripWith(byte[] luma, int width, int height, bool light)
    {
        var mask = new BackgroundMask(luma, width, height, light);
        int minGap = Math.Max(3, (int)Math.Round(StripMinGutter * width));
        int minBlock = Math.Max(3, (int)Math.Round(StripMinBlock * width));

        var blocks = new List<Region>();
        int y = 0;
        while (y < height)
        {
            while (y < height && mask.IsGutterRow(y, 0, width))
            {
                y++;
            }

            int start = y;
            int lastContent = y;
            // A block runs until an empty band at least minGap tall; shorter empty stretches (space inside a block) do not end it.
            int gapStart = -1;
            while (y < height)
            {
                if (mask.IsGutterRow(y, 0, width))
                {
                    if (gapStart < 0)
                    {
                        gapStart = y;
                    }

                    if (y - gapStart + 1 >= minGap)
                    {
                        break;
                    }
                }
                else
                {
                    gapStart = -1;
                    lastContent = y;
                }

                y++;
            }

            int end = lastContent + 1;
            if (end - start >= minBlock)
            {
                blocks.Add(Trim(mask, new Region(0, start, width, end - start)));
            }

            y = Math.Max(y + 1, end);
        }

        blocks.RemoveAll(b => b.Width <= 0 || b.Height <= 0);
        if (blocks.Count < 2 || blocks.Count > MaxStripBlocks)
        {
            return PagePanels.Whole;
        }

        return new PagePanels(blocks.Select(r => new PanelRect(r.X / (double)width, r.Y / (double)height, r.Width / (double)width, r.Height / (double)height)).ToList(), Confident: true);
    }

    private static bool BorderIsLight(byte[] luma, int width, int height)
    {
        long sum = 0;
        long count = 0;
        int bx = Math.Max(1, width / 50);
        int by = Math.Max(1, height / 50);
        for (int y = 0; y < height; y++)
        {
            bool edgeRow = y < by || y >= height - by;
            for (int x = 0; x < width; x++)
            {
                if (edgeRow || x < bx || x >= width - bx)
                {
                    sum += luma[(y * width) + x];
                    count++;
                }
            }
        }

        return count == 0 || (sum / (double)count) >= 128;
    }

    private static PagePanels DetectWith(byte[] luma, int width, int height, bool rightToLeft, bool light)
    {
        var mask = new BackgroundMask(luma, width, height, light);
        var leaves = new List<Region>();
        Split(mask, new Region(0, 0, width, height), rightToLeft, depth: 0, leaves);

        double pageArea = width * (double)height;
        var kept = leaves
            .Where(r => r.Area / pageArea >= MinPanelArea && r.Width / (double)width >= MinPanelSide && r.Height / (double)height >= MinPanelSide)
            .ToList();

        double coverage = kept.Sum(r => r.Area) / pageArea;
        if (kept.Count < 2 || kept.Count > MaxPanels || coverage < MinCoverage)
        {
            return PagePanels.Whole;
        }

        return new PagePanels(kept.Select(r => new PanelRect(r.X / (double)width, r.Y / (double)height, r.Width / (double)width, r.Height / (double)height)).ToList(), Confident: true);
    }

    private readonly record struct Region(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;

        public int Bottom => Y + Height;

        public long Area => (long)Width * Height;
    }

    /// <summary>Recursively cuts <paramref name="region"/> at its thickest gutter and appends the resulting leaves, in reading order, to <paramref name="leaves"/>.</summary>
    private static void Split(BackgroundMask mask, Region region, bool rightToLeft, int depth, List<Region> leaves)
    {
        region = Trim(mask, region);
        if (region.Width <= 0 || region.Height <= 0)
        {
            return;
        }

        double pageArea = mask.Width * (double)mask.Height;
        if (depth >= MaxDepth || region.Area / pageArea < MinPanelArea)
        {
            leaves.Add(region);
            return;
        }

        var horizontal = ThickestGutter(mask, region, rows: true);
        var vertical = ThickestGutter(mask, region, rows: false);
        int minRow = Math.Max(3, (int)Math.Round(MinGutter * mask.Height));
        int minCol = Math.Max(3, (int)Math.Round(MinGutter * mask.Width));
        bool canCutRows = horizontal.Thickness >= minRow;
        bool canCutCols = vertical.Thickness >= minCol;

        if (!canCutRows && !canCutCols)
        {
            leaves.Add(region);
            return;
        }

        // Rows first whenever a full-width gutter exists: comics read in rows, so a regular grid must come out row by row (a wider vertical gutter must not turn it into columns).
        // Columns are cut first only when no row cut is possible (a tall panel beside a stack).
        if (canCutRows)
        {
            var top = new Region(region.X, region.Y, region.Width, horizontal.Start - region.Y);
            var bottom = new Region(region.X, horizontal.Start + horizontal.Thickness, region.Width, region.Bottom - (horizontal.Start + horizontal.Thickness));
            Split(mask, top, rightToLeft, depth + 1, leaves);
            Split(mask, bottom, rightToLeft, depth + 1, leaves);
            return;
        }

        var left = new Region(region.X, region.Y, vertical.Start - region.X, region.Height);
        var right = new Region(vertical.Start + vertical.Thickness, region.Y, region.Right - (vertical.Start + vertical.Thickness), region.Height);
        if (rightToLeft)
        {
            Split(mask, right, rightToLeft, depth + 1, leaves);
            Split(mask, left, rightToLeft, depth + 1, leaves);
        }
        else
        {
            Split(mask, left, rightToLeft, depth + 1, leaves);
            Split(mask, right, rightToLeft, depth + 1, leaves);
        }
    }

    /// <summary>Shrinks <paramref name="region"/> so no edge row or column is gutter.</summary>
    private static Region Trim(BackgroundMask mask, Region region)
    {
        int x0 = region.X, y0 = region.Y, x1 = region.Right, y1 = region.Bottom;
        while (y0 < y1 && mask.IsGutterRow(y0, x0, x1)) y0++;
        while (y1 > y0 && mask.IsGutterRow(y1 - 1, x0, x1)) y1--;
        while (x0 < x1 && mask.IsGutterColumn(x0, y0, y1)) x0++;
        while (x1 > x0 && mask.IsGutterColumn(x1 - 1, y0, y1)) x1--;
        return new Region(x0, y0, x1 - x0, y1 - y0);
    }

    private readonly record struct Gutter(int Start, int Thickness);

    /// <summary>The thickest run of gutter rows (or columns) strictly inside the region; thickness 0 when there is none.</summary>
    private static Gutter ThickestGutter(BackgroundMask mask, Region region, bool rows)
    {
        int start = rows ? region.Y : region.X;
        int end = rows ? region.Bottom : region.Right;
        Gutter best = default;
        int runStart = -1;
        for (int i = start; i <= end; i++)
        {
            bool gutter = i < end && (rows ? mask.IsGutterRow(i, region.X, region.Right) : mask.IsGutterColumn(i, region.Y, region.Bottom));
            if (gutter)
            {
                if (runStart < 0)
                {
                    runStart = i;
                }

                continue;
            }

            if (runStart >= 0)
            {
                int thickness = i - runStart;
                if (thickness > best.Thickness)
                {
                    best = new Gutter(runStart, thickness);
                }

                runStart = -1;
            }
        }

        return best;
    }

    /// <summary>Which pixels are background, with an integral image so any row or column stretch is counted in O(1).</summary>
    private sealed class BackgroundMask
    {
        private readonly int[] _integral;
        private readonly int _stride;

        public BackgroundMask(byte[] luma, int width, int height, bool light)
        {
            Width = width;
            Height = height;
            _stride = width + 1;
            _integral = new int[_stride * (height + 1)];
            for (int y = 0; y < height; y++)
            {
                int rowSum = 0;
                for (int x = 0; x < width; x++)
                {
                    byte v = luma[(y * width) + x];
                    bool background = light ? v >= LightBackgroundLuma : v <= DarkBackgroundLuma;
                    rowSum += background ? 1 : 0;
                    _integral[((y + 1) * _stride) + x + 1] = _integral[(y * _stride) + x + 1] + rowSum;
                }
            }
        }

        public int Width { get; }

        public int Height { get; }

        private int Count(int x0, int y0, int x1, int y1) =>
            _integral[(y1 * _stride) + x1] - _integral[(y0 * _stride) + x1] - _integral[(y1 * _stride) + x0] + _integral[(y0 * _stride) + x0];

        public bool IsGutterRow(int y, int x0, int x1) => x1 > x0 && Count(x0, y, x1, y + 1) >= GutterFraction * (x1 - x0);

        public bool IsGutterColumn(int x, int y0, int y1) => y1 > y0 && Count(x, y0, x + 1, y1) >= GutterFraction * (y1 - y0);
    }
}
