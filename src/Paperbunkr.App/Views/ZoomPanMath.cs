using System;
using Avalonia;

namespace Paperbunkr.App.Views;

/// <summary>
/// Pure zoom/pan geometry shared by <see cref="PageCanvas"/> (gesture handling) and
/// <see cref="ReaderPageDrawOperation"/> (render-time <c>destRect</c>), so pan-clamping and
/// image-placement math can't drift apart. Per docs/superpowers/specs/
/// 2026-08-09-reader-gestures-and-grid-navigation-design.md.
/// </summary>
public static class ZoomPanMath
{
    public const double MinZoom = 1.0;
    public const double MaxZoom = 4.0;
    public const double DoubleClickZoom = 2.0;

    public static double ComputeBaseScale(Size canvasSize, PixelSize imagePixelSize)
    {
        if (canvasSize.Width <= 0 || canvasSize.Height <= 0 || imagePixelSize.Width <= 0 || imagePixelSize.Height <= 0)
        {
            return 0;
        }

        return Math.Min(canvasSize.Width / imagePixelSize.Width, canvasSize.Height / imagePixelSize.Height);
    }

    public static double ClampZoom(double zoom) => Math.Clamp(zoom, MinZoom, MaxZoom);

    public static (double X, double Y) ClampPan(Size canvasSize, PixelSize imagePixelSize, double zoom, double proposedX, double proposedY)
    {
        double baseScale = ComputeBaseScale(canvasSize, imagePixelSize);
        double displayedW = imagePixelSize.Width * baseScale * zoom;
        double displayedH = imagePixelSize.Height * baseScale * zoom;
        double maxPanX = Math.Max(0, (displayedW - canvasSize.Width) / 2);
        double maxPanY = Math.Max(0, (displayedH - canvasSize.Height) / 2);

        return (Math.Clamp(proposedX, -maxPanX, maxPanX), Math.Clamp(proposedY, -maxPanY, maxPanY));
    }

    /// <summary>
    /// Double-click: the clicked point becomes the new canvas center at <paramref name="targetZoom"/>.
    /// Only meaningful when called from the unzoomed (zoom=1, pan=0) state - double-click-to-zoom-in
    /// is only reachable when <c>ZoomLevel == 1.0</c>, per spec.
    /// </summary>
    public static (double X, double Y) PanToCenterOn(Size canvasSize, PixelSize imagePixelSize, double targetZoom, Point clickPoint)
    {
        double baseScale = ComputeBaseScale(canvasSize, imagePixelSize);
        double baseW = imagePixelSize.Width * baseScale;
        double baseH = imagePixelSize.Height * baseScale;
        double imageLeft = (canvasSize.Width - baseW) / 2;
        double imageTop = (canvasSize.Height - baseH) / 2;

        double fx = baseW > 0 ? Math.Clamp((clickPoint.X - imageLeft) / baseW, 0, 1) : 0.5;
        double fy = baseH > 0 ? Math.Clamp((clickPoint.Y - imageTop) / baseH, 0, 1) : 0.5;

        double displayedW = baseW * targetZoom;
        double displayedH = baseH * targetZoom;
        double panX = displayedW * (0.5 - fx);
        double panY = displayedH * (0.5 - fy);

        return ClampPan(canvasSize, imagePixelSize, targetZoom, panX, panY);
    }

    /// <summary>
    /// Ctrl+wheel/pinch: the image point currently under <paramref name="cursorPoint"/> stays under
    /// the cursor as zoom changes from <paramref name="currentZoom"/>/<paramref name="currentPan"/>
    /// to <paramref name="targetZoom"/> - works incrementally from any current state (unlike
    /// <see cref="PanToCenterOn"/>), which is what makes wheel-zoom feel anchored instead of having
    /// the image drift under the cursor.
    /// </summary>
    public static (double X, double Y) PanToKeepPointFixed(Size canvasSize, PixelSize imagePixelSize, double currentZoom, Point currentPan, Point cursorPoint, double targetZoom)
    {
        double baseScale = ComputeBaseScale(canvasSize, imagePixelSize);
        double displayedW0 = imagePixelSize.Width * baseScale * currentZoom;
        double displayedH0 = imagePixelSize.Height * baseScale * currentZoom;
        double imageLeft0 = ((canvasSize.Width - displayedW0) / 2) + currentPan.X;
        double imageTop0 = ((canvasSize.Height - displayedH0) / 2) + currentPan.Y;

        double u = displayedW0 > 0 ? Math.Clamp((cursorPoint.X - imageLeft0) / displayedW0, 0, 1) : 0.5;
        double v = displayedH0 > 0 ? Math.Clamp((cursorPoint.Y - imageTop0) / displayedH0, 0, 1) : 0.5;

        double displayedW1 = imagePixelSize.Width * baseScale * targetZoom;
        double displayedH1 = imagePixelSize.Height * baseScale * targetZoom;
        double panX1 = cursorPoint.X - (u * displayedW1) - ((canvasSize.Width - displayedW1) / 2);
        double panY1 = cursorPoint.Y - (v * displayedH1) - ((canvasSize.Height - displayedH1) / 2);

        return ClampPan(canvasSize, imagePixelSize, targetZoom, panX1, panY1);
    }
}
