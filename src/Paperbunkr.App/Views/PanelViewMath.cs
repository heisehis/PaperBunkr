using System;
using Avalonia;
using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// Turns a detected panel into the zoom and pan that show it (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md sections 3 and 4). Pure geometry on top of
/// <see cref="ZoomPanMath"/>, in the same conventions (zoom is a multiplier over the fit-mode base scale; pan is the offset of the page centre from the canvas centre).
/// </summary>
public static class PanelViewMath
{
    /// <summary>Breathing room around a panel: this fraction of the viewport on each side.</summary>
    public const double Margin = 0.04;

    /// <summary>
    /// The zoom and pan that fit <paramref name="panel"/> into <paramref name="viewport"/> with a <see cref="Margin"/> around it. <paramref name="content"/> is the page's effective (post-rotation) pixel size
    /// and <paramref name="panel"/> is in the same orientation (see <see cref="RotateRect"/>). Zoom is clamped to [100%, <see cref="ZoomPanMath.MaxZoom"/>]: a panel as big as the page shows the page, a tiny
    /// one is enlarged at most 400%.
    /// </summary>
    public static (double Zoom, double PanX, double PanY) ForPanel(PanelRect panel, PixelSize content, Size viewport, ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false)
    {
        double baseScale = ZoomPanMath.ComputeBaseScale(viewport, content, fitMode, fitOnlyIfOversized);
        if (baseScale <= 0 || panel.Width <= 0 || panel.Height <= 0)
        {
            return (ZoomPanMath.FitZoom, 0, 0);
        }

        double panelW = panel.Width * content.Width * baseScale;
        double panelH = panel.Height * content.Height * baseScale;
        double room = 1 - (2 * Margin);
        double zoom = Math.Min(viewport.Width * room / panelW, viewport.Height * room / panelH);
        zoom = Math.Clamp(zoom, ZoomPanMath.FitZoom, ZoomPanMath.MaxZoom);

        double displayedW = content.Width * baseScale * zoom;
        double displayedH = content.Height * baseScale * zoom;
        double panX = displayedW * (0.5 - panel.CenterX);
        double panY = displayedH * (0.5 - panel.CenterY);
        var (x, y) = ZoomPanMath.ClampPan(viewport, content, zoom, panX, panY, fitMode, fitOnlyIfOversized);
        return (zoom, x, y);
    }

    private const double MinSliceViewport = 100;

    /// <summary>The most slices one panel is cut into along an axis (a page 100 screens tall is still stepped through, just in coarser pieces).</summary>
    private const int MaxSlicesPerAxis = 200;

    /// <summary>How much neighbouring slices of an oversized panel overlap, as a fraction of a slice, so reading on from one to the next never skips a line.</summary>
    public const double SliceOverlap = 0.1;

    /// <summary>
    /// Cuts every panel that is bigger than <paramref name="viewport"/> at 100% (it would not fit even with no extra zoom, as with a tall panel on a long strip or any page in fit-width mode) into overlapping
    /// slices that each do, in reading order: top to bottom, and left to right (right to left when <paramref name="rightToLeft"/>) within a row. Panels that fit are returned as they are.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<PanelRect> SplitOversized(System.Collections.Generic.IReadOnlyList<PanelRect> panels, PixelSize content, Size viewport, ImageFitMode fitMode, bool fitOnlyIfOversized, bool rightToLeft)
    {
        double baseScale = ZoomPanMath.ComputeBaseScale(viewport, content, fitMode, fitOnlyIfOversized);
        if (baseScale <= 0 || panels.Count == 0)
        {
            return panels;
        }

        var result = new System.Collections.Generic.List<PanelRect>(panels.Count);
        bool anySplit = false;
        foreach (var panel in panels)
        {
            int cols = SliceCount(panel.Width * content.Width * baseScale, viewport.Width);
            int rows = SliceCount(panel.Height * content.Height * baseScale, viewport.Height);
            if (cols == 1 && rows == 1)
            {
                result.Add(panel);
                continue;
            }

            anySplit = true;
            double sliceW = SliceSize(panel.Width, cols);
            double sliceH = SliceSize(panel.Height, rows);
            for (int row = 0; row < rows; row++)
            {
                double y = rows == 1 ? panel.Y : panel.Y + (row * (panel.Height - sliceH) / (rows - 1));
                for (int step = 0; step < cols; step++)
                {
                    int col = rightToLeft ? cols - 1 - step : step;
                    double x = cols == 1 ? panel.X : panel.X + (col * (panel.Width - sliceW) / (cols - 1));
                    result.Add(new PanelRect(x, y, sliceW, sliceH));
                }
            }
        }

        return anySplit ? result : panels;
    }

    /// <summary>
    /// How many overlapping slices it takes to cover a panel of <paramref name="size"/> screen pixels along one axis of a viewport that is <paramref name="viewportSize"/> along it. A panel that fills the
    /// viewport (a full-width panel in fit-width mode) is one slice; a bigger one is cut into slices a little smaller than the viewport.
    /// </summary>
    private static int SliceCount(double size, double viewportSize)
    {
        // A viewport too small to read anything in (mid-layout, collapsing) is not worth slicing for: it would ask for thousands of slices.
        if (size <= viewportSize * 1.03 || viewportSize < MinSliceViewport)
        {
            return 1;
        }

        // n slices of s with SliceOverlap between neighbours cover s * (1 + (n - 1) * (1 - overlap)).
        double target = viewportSize * (1 - Margin);
        return Math.Clamp((int)Math.Ceiling(((size / target) - 1) / (1 - SliceOverlap)) + 1, 2, MaxSlicesPerAxis);
    }

    /// <summary>The slice size (as a fraction of the panel) so that <paramref name="count"/> slices with the overlap cover the whole panel.</summary>
    private static double SliceSize(double whole, int count) => count == 1 ? whole : whole / (1 + ((count - 1) * (1 - SliceOverlap)));

    /// <summary>
    /// Maps a rectangle detected on the page as stored to the orientation it is shown in, after a clockwise rotation of <paramref name="degrees"/> (0, 90, 180 or 270). The order of a page's panels
    /// is not changed by rotating.
    /// </summary>
    public static PanelRect RotateRect(PanelRect r, int degrees) => (((degrees % 360) + 360) % 360) switch
    {
        90 => new PanelRect(1 - r.Y - r.Height, r.X, r.Height, r.Width),
        180 => new PanelRect(1 - r.X - r.Width, 1 - r.Y - r.Height, r.Width, r.Height),
        270 => new PanelRect(r.Y, 1 - r.X - r.Width, r.Height, r.Width),
        _ => r,
    };

    /// <summary>The point of the page (fractions 0-1) currently under a canvas point, given the displayed zoom and pan, or null when the point is off the page.</summary>
    public static Point? PagePointAt(Point canvasPoint, Size viewport, PixelSize content, double zoom, double panX, double panY, ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false)
    {
        double baseScale = ZoomPanMath.ComputeBaseScale(viewport, content, fitMode, fitOnlyIfOversized);
        double displayedW = content.Width * baseScale * zoom;
        double displayedH = content.Height * baseScale * zoom;
        if (displayedW <= 0 || displayedH <= 0)
        {
            return null;
        }

        double left = ((viewport.Width - displayedW) / 2) + panX;
        double top = ((viewport.Height - displayedH) / 2) + panY;
        double fx = (canvasPoint.X - left) / displayedW;
        double fy = (canvasPoint.Y - top) / displayedH;
        return fx is < 0 or > 1 || fy is < 0 or > 1 ? null : new Point(fx, fy);
    }

    /// <summary>The panel currently in view: the one containing the viewport's centre, else the nearest by centre, given the current zoom and pan. -1 when there are no panels.</summary>
    public static int CurrentPanelIndex(System.Collections.Generic.IReadOnlyList<PanelRect> panels, Size viewport, PixelSize content, double zoom, double panX, double panY, ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false)
    {
        if (panels.Count == 0)
        {
            return -1;
        }

        double baseScale = ZoomPanMath.ComputeBaseScale(viewport, content, fitMode, fitOnlyIfOversized);
        double displayedW = content.Width * baseScale * zoom;
        double displayedH = content.Height * baseScale * zoom;
        if (displayedW <= 0 || displayedH <= 0)
        {
            return 0;
        }

        // The page point at the centre of the viewport.
        double cx = 0.5 - (panX / displayedW);
        double cy = 0.5 - (panY / displayedH);
        return PanelStepper.IndexAt(panels, cx, cy);
    }
}
