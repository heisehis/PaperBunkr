using System;
using Paperbunkr.App.Services.Reader.Panels;
using SkiaSharp;

namespace Paperbunkr.App.Services.Reader;

/// <summary>What the reader pipeline does to a page's pixels before it is cached (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md #2 auto-levels and #3 auto-crop).</summary>
/// <param name="AutoLevels">Stretch a washed-out page's levels.</param>
/// <param name="AutoCrop">Trim the page's scan borders (unless a per-page override says otherwise).</param>
public readonly record struct PageProcessingOptions(bool AutoLevels, bool AutoCrop)
{
    public static readonly PageProcessingOptions None = new(false, false);
}

/// <summary>A page's black and white points (0-1 of the grey range) for an auto-levels stretch.</summary>
public readonly record struct PageLevels(float BlackPoint, float WhitePoint);

/// <summary>The part of a page kept by auto-crop: how much to trim from each side, as a fraction of the page's width (left and right) or height (top and bottom).</summary>
public readonly record struct PageCropRect(double Left, double Top, double Right, double Bottom)
{
    /// <summary>The kept rectangle in source pixels.</summary>
    public SKRectI ToSourceRect(int width, int height)
    {
        int left = (int)Math.Round(Left * width);
        int top = (int)Math.Round(Top * height);
        int right = width - (int)Math.Round(Right * width);
        int bottom = height - (int)Math.Round(Bottom * height);
        return new SKRectI(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }
}

/// <summary>What was decided for one page, remembered so the detail tier and any re-decode of the same page use exactly the same crop and levels.</summary>
public sealed record PageProcessingResult(PageCropRect? Crop, PageLevels? Levels)
{
    public static readonly PageProcessingResult Nothing = new(null, null);

    public bool IsNothing => Crop is null && Levels is null;
}

/// <summary>A small luminance copy of a page (the longest side at most <see cref="PanelDetector.AnalysisSize"/>), shared by everything that analyses a page's pixels: auto-levels and auto-crop.</summary>
public sealed record PageLumaGrid(byte[] Luma, int Width, int Height)
{
    /// <summary>Reduces <paramref name="page"/> to a grey grid (transparent pixels count as white paper, like the panel analyser).</summary>
    public static PageLumaGrid From(SKBitmap page)
    {
        SKBitmap? resized = null;
        try
        {
            var source = page;
            int longSide = Math.Max(page.Width, page.Height);
            if (longSide > PanelDetector.AnalysisSize)
            {
                double scale = PanelDetector.AnalysisSize / (double)longSide;
                resized = page.Resize(new SKImageInfo(Math.Max(16, (int)Math.Round(page.Width * scale)), Math.Max(16, (int)Math.Round(page.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                source = resized ?? page;
            }

            var luma = new byte[source.Width * source.Height];
            var pixels = source.Pixels;
            for (int i = 0; i < luma.Length; i++)
            {
                var c = pixels[i];
                double alpha = c.Alpha / 255.0;
                double value = (((0.299 * c.Red) + (0.587 * c.Green) + (0.114 * c.Blue)) * alpha) + (255 * (1 - alpha));
                luma[i] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
            }

            return new PageLumaGrid(luma, source.Width, source.Height);
        }
        finally
        {
            resized?.Dispose();
        }
    }
}

/// <summary>
/// Auto-levels: ComicRackCE's <c>Histogram.GetBlackPointNormalized</c>/<c>GetWhitePointNormalized</c> rule on a page's grey histogram (<c>_reference/ComicRackCE/cYo.Common/Drawing/Histogram.cs</c>): the black point is the
/// lowest grey level holding the first 0.5% of pixels, capped at 0.25; the white point the highest holding the last 0.5%, floored at 0.75. Like CE, it only acts on a page that is washed out
/// (<c>whitePoint &lt; 0.95</c> or <c>blackPoint &gt; 0.05</c>) - a page that already spans the range is left exactly as it is.
/// </summary>
public static class PageLevelsAnalyzer
{
    private const float Threshold = 0.005f;
    private const float Range = 0.25f;

    /// <summary>The levels to apply, or null when the page needs none. <paramref name="region"/> (grid pixels) limits the histogram to the part of the page that will be shown.</summary>
    public static PageLevels? Analyze(PageLumaGrid grid, (int Left, int Top, int Right, int Bottom)? region = null)
    {
        var (left, top, right, bottom) = region ?? (0, 0, grid.Width, grid.Height);
        left = Math.Clamp(left, 0, grid.Width);
        right = Math.Clamp(right, left, grid.Width);
        top = Math.Clamp(top, 0, grid.Height);
        bottom = Math.Clamp(bottom, top, grid.Height);
        long count = (long)(right - left) * (bottom - top);
        if (count <= 0)
        {
            return null;
        }

        var histogram = new long[256];
        for (int y = top; y < bottom; y++)
        {
            int row = y * grid.Width;
            for (int x = left; x < right; x++)
            {
                histogram[grid.Luma[row + x]]++;
            }
        }

        float black = LowThreshold(histogram, count);
        float white = TopThreshold(histogram, count);
        black = Math.Min(black, Range);
        white = Math.Max(white, 3 * Range);
        return ImageAdjustmentMath.NeedsLevels(black, white) ? new PageLevels(black, white) : null;
    }

    private static float LowThreshold(long[] histogram, long count)
    {
        double sum = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            sum += histogram[i] / (double)count;
            if (sum >= Threshold)
            {
                return i / (float)histogram.Length;
            }
        }

        return 0f;
    }

    private static float TopThreshold(long[] histogram, long count)
    {
        double sum = 0;
        for (int i = histogram.Length - 1; i >= 0; i--)
        {
            sum += histogram[i] / (double)count;
            if (sum >= Threshold)
            {
                return i / (float)histogram.Length;
            }
        }

        return 1f;
    }
}

/// <summary>
/// Auto-crop: finds the plain white or black border of a scanned page on its <see cref="PageLumaGrid"/> so the reader can drop it (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md #3).
/// The border colour is read from the page's outer ring; the crop walks in from each edge until a row or column holds real content. Safeguards keep it from ever doing harm: each side is capped, a page
/// with no uniform light or dark border (a photo, a full-bleed page, mid-grey) is left alone, a page that is nearly all border is left alone, and a small margin of the border is kept.
/// </summary>
public static class PageCropDetector
{
    /// <summary>The ring around the page (fraction of each dimension) that must agree on the border colour.</summary>
    public const double RingFraction = 0.02;

    /// <summary>How far (0-255) a grey level may differ from the border colour and still count as border.</summary>
    public const int BorderTolerance = 40;

    /// <summary>The share of the ring that must be within <see cref="BorderTolerance"/> of the border colour for the page to have a uniform border at all.</summary>
    public const double RingAgreement = 0.90;

    /// <summary>A row or column is content when at least this share of it is not border colour (and at least 3 grid pixels), so a speck of dust does not stop the walk.</summary>
    public const double NonBorderFraction = 0.02;

    /// <summary>The most that is trimmed from any one side (fraction of the page).</summary>
    public const double MaxSideFraction = 0.15;

    /// <summary>A page whose content (before any cap) covers less than this share of its area is left alone: a mostly blank page with one small element is not a bordered scan.</summary>
    public const double MinContent = 0.25;

    /// <summary>Border kept around the content on each side (fraction of the page), so panel edges are never clipped.</summary>
    public const double KeepMargin = 0.02;

    /// <summary>A side with less trim than this is left alone: not worth a resample.</summary>
    public const double MinTrim = 0.015;

    public static PageCropRect? Detect(PageLumaGrid grid)
    {
        int w = grid.Width, h = grid.Height;
        if (w < 16 || h < 16)
        {
            return null;
        }

        int ringX = Math.Max(1, (int)Math.Round(w * RingFraction));
        int ringY = Math.Max(1, (int)Math.Round(h * RingFraction));
        var ring = new System.Collections.Generic.List<byte>();
        for (int y = 0; y < h; y++)
        {
            bool edgeRow = y < ringY || y >= h - ringY;
            for (int x = 0; x < w; x++)
            {
                if (edgeRow || x < ringX || x >= w - ringX)
                {
                    ring.Add(grid.Luma[(y * w) + x]);
                }
            }
        }

        ring.Sort();
        int border = ring[ring.Count / 2];
        if (border is > 60 and < 195)
        {
            return null;   // mid-grey: not a scan border
        }

        int agree = 0;
        foreach (byte value in ring)
        {
            if (Math.Abs(value - border) <= BorderTolerance)
            {
                agree++;
            }
        }

        if (agree < ring.Count * RingAgreement)
        {
            return null;   // the outer ring is not uniform: art runs to the edge
        }

        bool IsContent(int x, int y) => Math.Abs(grid.Luma[(y * w) + x] - border) > BorderTolerance;
        int minRowHits = Math.Max(3, (int)Math.Ceiling(w * NonBorderFraction));
        int minColHits = Math.Max(3, (int)Math.Ceiling(h * NonBorderFraction));

        int RowHits(int y)
        {
            int n = 0;
            for (int x = 0; x < w; x++)
            {
                if (IsContent(x, y)) n++;
            }

            return n;
        }

        int ColHits(int x)
        {
            int n = 0;
            for (int y = 0; y < h; y++)
            {
                if (IsContent(x, y)) n++;
            }

            return n;
        }

        int top = 0;
        while (top < h && RowHits(top) < minRowHits) top++;
        if (top >= h)
        {
            return null;   // no content at all: a blank page
        }

        int bottom = h - 1;
        while (bottom > top && RowHits(bottom) < minRowHits) bottom--;
        int left = 0;
        while (left < w && ColHits(left) < minColHits) left++;
        int right = w - 1;
        while (right > left && ColHits(right) < minColHits) right--;

        double insetLeft = left / (double)w;
        double insetTop = top / (double)h;
        double insetRight = (w - 1 - right) / (double)w;
        double insetBottom = (h - 1 - bottom) / (double)h;

        if ((1 - insetLeft - insetRight) * (1 - insetTop - insetBottom) < MinContent)
        {
            return null;
        }

        insetLeft = Trim(insetLeft);
        insetTop = Trim(insetTop);
        insetRight = Trim(insetRight);
        insetBottom = Trim(insetBottom);
        if (insetLeft == 0 && insetTop == 0 && insetRight == 0 && insetBottom == 0)
        {
            return null;
        }

        return new PageCropRect(insetLeft, insetTop, insetRight, insetBottom);
    }

    /// <summary>Keeps <see cref="KeepMargin"/> of border, caps the trim at <see cref="MaxSideFraction"/>, and drops a trim too small to matter.</summary>
    private static double Trim(double inset)
    {
        double trimmed = Math.Min(Math.Max(0, inset - KeepMargin), MaxSideFraction);
        return trimmed < MinTrim ? 0 : trimmed;
    }
}

/// <summary>Applies a page's crop and levels to its pixels (Skia, full resolution) - the one place the pipeline turns an analysis into a new bitmap.</summary>
public static class PageImageProcessor
{
    /// <summary>Analyses <paramref name="page"/> for the enabled steps. <paramref name="crop"/> false skips the crop entirely (a page overridden to "never").</summary>
    public static PageProcessingResult Analyze(SKBitmap page, bool levels, bool crop)
    {
        if (!levels && !crop)
        {
            return PageProcessingResult.Nothing;
        }

        var grid = PageLumaGrid.From(page);
        var cropRect = crop ? PageCropDetector.Detect(grid) : null;
        PageLevels? pageLevels = null;
        if (levels)
        {
            (int, int, int, int)? region = cropRect is { } c
                ? ((int)Math.Round(c.Left * grid.Width), (int)Math.Round(c.Top * grid.Height), grid.Width - (int)Math.Round(c.Right * grid.Width), grid.Height - (int)Math.Round(c.Bottom * grid.Height))
                : null;
            pageLevels = PageLevelsAnalyzer.Analyze(grid, region);
        }

        return new PageProcessingResult(cropRect, pageLevels);
    }

    /// <summary>The processed copy of <paramref name="page"/>, or the page itself when there is nothing to apply. The caller disposes whichever comes back if it is not the page.</summary>
    public static SKBitmap Apply(SKBitmap page, PageProcessingResult result)
    {
        if (result.IsNothing)
        {
            return page;
        }

        var source = result.Crop is { } crop ? crop.ToSourceRect(page.Width, page.Height) : new SKRectI(0, 0, page.Width, page.Height);
        var target = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(target);
        using var image = SKImage.FromBitmap(page);
        using var paint = new SKPaint();
        if (result.Levels is { } levels)
        {
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(ImageAdjustmentMath.CreateLevelsMatrix(levels.BlackPoint, levels.WhitePoint));
        }

        // A straight copy with no scaling: the crop is a source rectangle, the levels a colour filter.
        canvas.DrawImage(image, new SKRect(source.Left, source.Top, source.Right, source.Bottom), new SKRect(0, 0, source.Width, source.Height), new SKSamplingOptions(SKFilterMode.Nearest), paint);
        return target;
    }
}
