using System;
using Avalonia;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// Pure "split-page part navigation" math (docs/superpowers/specs/2026-09-05-reader-polish-
/// backlog-finish-design.md §1) - ported from ComicRackCE's real (and, contrary to a stale
/// docs/ce-feature-inventory.md claim, genuinely present) part-grid mechanism: when a page is
/// zoomed past what the viewport shows, it's split into a grid of viewport-sized tiles ("parts"),
/// and Next/Previous-page steps through those tiles in reading order before actually turning the
/// page. Same shape as <see cref="ZoomPanMath"/>/<see cref="SpreadLayoutMath"/> - plain value
/// types, no Avalonia app context beyond <see cref="Size"/>/<see cref="PixelSize"/>.
///
/// <paramref name="content"/> everywhere below is whatever content is currently being displayed at
/// full resolution - a solo page's own <see cref="PixelSize"/>, or (for a double-page spread)
/// <see cref="SpreadLayoutMath.ComputeCombinedSize"/>'s combined virtual size - callers decide
/// which, this file has no opinion.
/// </summary>
internal static class PagePartMath
{
    /// <summary>
    /// The grid of viewport-sized tiles the content is split into at the given zoom/fit. <c>(1, 1)</c>
    /// when the scaled content fits the viewport in both dimensions - mirrors
    /// <see cref="ZoomPanMath.HasOverflow"/>'s own epsilon-guarded check rather than reinventing a
    /// second one, so "no overflow" and "single part" never disagree.
    /// </summary>
    public static (int Cols, int Rows) ComputePartGrid(Size viewport, PixelSize content, double zoom, ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false)
    {
        if (!ZoomPanMath.HasOverflow(viewport, content, zoom, fitMode, fitOnlyIfOversized))
        {
            return (1, 1);
        }

        double scale = ZoomPanMath.ComputeBaseScale(viewport, content, fitMode, fitOnlyIfOversized) * zoom;
        double displayedW = content.Width * scale;
        double displayedH = content.Height * scale;

        int cols = Math.Max(1, (int)Math.Ceiling(displayedW / viewport.Width));
        int rows = Math.Max(1, (int)Math.Ceiling(displayedH / viewport.Height));
        return (cols, rows);
    }

    public static int PartCount((int Cols, int Rows) grid) => grid.Cols * grid.Rows;

    /// <summary>
    /// The world-space rect (in unscaled content pixels, top-left origin) of grid cell
    /// <paramref name="partIndex"/> - row-major reading order, left-to-right unless
    /// <paramref name="rightToLeft"/> reverses column order within each row. Cells at the right/
    /// bottom edge of the grid are clipped to the content's actual remaining size (the last column/
    /// row is rarely a whole viewport-width/height, since <see cref="ComputePartGrid"/> ceils).
    /// </summary>
    private static Rect GetPartRect(int partIndex, (int Cols, int Rows) grid, Size viewport, PixelSize content, double scale, bool rightToLeft)
    {
        int row = partIndex / grid.Cols;
        int colInRow = partIndex % grid.Cols;
        int col = rightToLeft ? grid.Cols - 1 - colInRow : colInRow;

        double tileW = viewport.Width / scale;
        double tileH = viewport.Height / scale;

        double x = Math.Min(col * tileW, Math.Max(0, content.Width - tileW));
        double y = Math.Min(row * tileH, Math.Max(0, content.Height - tileH));
        double w = Math.Min(tileW, content.Width - x);
        double h = Math.Min(tileH, content.Height - y);
        return new Rect(x, y, w, h);
    }

    /// <summary>
    /// Given the *current* pan offset, which grid cell it's nearest to - mirrors CE's own
    /// <c>GetBestPartFit</c>. Deriving "current part" from pan offset (rather than tracking separate
    /// mutable state) means a free mouse-drag pan between page-turns is still respected: the next
    /// Next/Previous-page press steps from wherever the user actually panned to, not from a stale
    /// tracked index.
    /// </summary>
    public static int FindNearestPart((int Cols, int Rows) grid, Size viewport, PixelSize content, double scale, double panX, double panY, bool rightToLeft)
    {
        int count = PartCount(grid);
        if (count <= 1)
        {
            return 0;
        }

        // Pan is stored as an offset from center (ZoomPanMath's convention); convert to the
        // top-left content-space point currently at the viewport's center.
        double displayedW = content.Width * scale;
        double displayedH = content.Height * scale;
        double contentLeft = (displayedW - viewport.Width) / 2 - panX;
        double contentTop = (displayedH - viewport.Height) / 2 - panY;
        double centerX = (contentLeft + viewport.Width / 2) / scale;
        double centerY = (contentTop + viewport.Height / 2) / scale;

        int best = 0;
        double bestDistanceSq = double.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var rect = GetPartRect(i, grid, viewport, content, scale, rightToLeft);
            double cx = rect.X + rect.Width / 2;
            double cy = rect.Y + rect.Height / 2;
            double dx = cx - centerX;
            double dy = cy - centerY;
            double distanceSq = dx * dx + dy * dy;
            if (distanceSq < bestDistanceSq)
            {
                bestDistanceSq = distanceSq;
                best = i;
            }
        }

        return best;
    }

    /// <summary>The pan offset (<see cref="ZoomPanMath"/>'s center-relative convention) that centers grid cell <paramref name="partIndex"/>, clamped via <see cref="ZoomPanMath.ClampPan"/> so it never differs from every other pan writer's own bounds.</summary>
    public static (double X, double Y) PanForPart(int partIndex, (int Cols, int Rows) grid, Size viewport, PixelSize content, double scale, double zoom, bool rightToLeft, ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false)
    {
        var rect = GetPartRect(partIndex, grid, viewport, content, scale, rightToLeft);
        double cellCenterX = rect.X + rect.Width / 2;
        double cellCenterY = rect.Y + rect.Height / 2;
        double contentCenterX = content.Width / 2.0;
        double contentCenterY = content.Height / 2.0;

        double panX = (contentCenterX - cellCenterX) * scale;
        double panY = (contentCenterY - cellCenterY) * scale;
        return ZoomPanMath.ClampPan(viewport, content, zoom, panX, panY, fitMode, fitOnlyIfOversized);
    }
}
