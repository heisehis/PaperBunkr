using System;
using Avalonia;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// Pure zoom/pan geometry shared by <see cref="PageCanvas"/> (gesture handling) and
/// <see cref="ReaderPageDrawOperation"/> (render-time <c>destRect</c>), so pan-clamping and
/// image-placement math can't drift apart. Per docs/superpowers/specs/
/// 2026-08-09-reader-gestures-and-grid-navigation-design.md and (fit-mode/rotation)
/// docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-controls-design.md §2/§4.
///
/// <paramref name="imagePixelSize"/> everywhere below is the image's *effective* (post-rotation)
/// pixel size - callers rotating a page 90/270 degrees swap width/height before calling in, so this
/// file itself never needs to know about rotation at all, only about the shape it's fitting.
///
/// <c>fitMode</c>/<c>fitOnlyIfOversized</c> default to <see cref="ImageFitMode.Fit"/>/<c>false</c>
/// on every method - exactly reproduces this file's original always-<c>min(widthRatio,
/// heightRatio)</c>-never-skip behavior, so the Novels PDF reader (which shares this exact math via
/// <see cref="PageCanvas"/>/<see cref="ReaderPageDrawOperation"/> but has no fit-mode concept of its
/// own) needs zero changes.
/// </summary>
public static class ZoomPanMath
{
    public const double MinZoom = 1.0;
    public const double MaxZoom = 4.0;
    public const double DoubleClickZoom = 2.0;

    /// <summary>
    /// Per-mode formulas and the <paramref name="fitOnlyIfOversized"/> early-return checks are
    /// ported from ComicRackCE's <c>ImageDisplayControl.GetScale</c>, confirmed from source rather
    /// than assumed - notably <see cref="ImageFitMode.Fit"/> only skips fitting when the canvas
    /// already fits the image in *both* dimensions (<c>&amp;&amp;</c>), while
    /// <see cref="ImageFitMode.BestFit"/> skips it if *either* dimension already fits (<c>||</c>) -
    /// a real asymmetry in CE's own source, not a typo here.
    /// </summary>
    public static double ComputeBaseScale(Size canvasSize, PixelSize imagePixelSize, ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false)
    {
        if (canvasSize.Width <= 0 || canvasSize.Height <= 0 || imagePixelSize.Width <= 0 || imagePixelSize.Height <= 0)
        {
            return 0;
        }

        double widthRatio = canvasSize.Width / imagePixelSize.Width;
        double heightRatio = canvasSize.Height / imagePixelSize.Height;
        bool widthFits = canvasSize.Width > imagePixelSize.Width;
        bool heightFits = canvasSize.Height > imagePixelSize.Height;

        return fitMode switch
        {
            ImageFitMode.Original => 1.0,
            ImageFitMode.FitWidth => fitOnlyIfOversized && widthFits ? 1.0 : widthRatio,
            ImageFitMode.FitHeight => fitOnlyIfOversized && heightFits ? 1.0 : heightRatio,
            ImageFitMode.BestFit => fitOnlyIfOversized && (widthFits || heightFits) ? 1.0 : Math.Max(widthRatio, heightRatio),
            ImageFitMode.Fit or _ => fitOnlyIfOversized && widthFits && heightFits ? 1.0 : Math.Min(widthRatio, heightRatio),
        };
    }

    public static double ClampZoom(double zoom) => Math.Clamp(zoom, MinZoom, MaxZoom);

    /// <summary>
    /// Whether the current fit-mode/zoom combination makes the displayed content bigger than the
    /// canvas in either dimension - the real pan-enable gate, not <c>zoom &gt; MinZoom</c> alone.
    /// Before fit modes existed, the only base scale was <see cref="ImageFitMode.Fit"/> (always
    /// contain-within-bounds), so overflow could only ever come from the user's own zoom multiplier
    /// - "can pan" and "is zoomed in" were the same question. <see cref="ImageFitMode.Original"/>
    /// (native pixel size, usually bigger than the viewport) and <see cref="ImageFitMode.FitWidth"/>/
    /// <see cref="ImageFitMode.FitHeight"/>/<see cref="ImageFitMode.BestFit"/> (constrain only one
    /// dimension, or deliberately overflow) can all overflow at <c>zoom == MinZoom</c> too - without
    /// this check, that overflow is simply unreachable, stuck cut off with no way to pan to it.
    /// </summary>
    public static bool HasOverflow(Size canvasSize, PixelSize imagePixelSize, double zoom, ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false)
    {
        double baseScale = ComputeBaseScale(canvasSize, imagePixelSize, fitMode, fitOnlyIfOversized);
        double displayedW = imagePixelSize.Width * baseScale * zoom;
        double displayedH = imagePixelSize.Height * baseScale * zoom;

        // Small epsilon - an exact width/height match (e.g. Fit's own contain guarantee) shouldn't
        // register as overflow due to floating-point rounding.
        const double epsilon = 0.5;
        return displayedW > canvasSize.Width + epsilon || displayedH > canvasSize.Height + epsilon;
    }

    /// <summary>
    /// Composes a manual rotation with auto-rotate's contribution, wrapped to [0, 360). Ported
    /// faithfully from ComicRackCE (docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-
    /// controls-design.md §4): auto-rotate only fires when the *native* (unrotated) page bitmap is
    /// landscape, composing -90 with whatever manual rotation is active rather than replacing it.
    /// </summary>
    public static int ComposeRotationDegrees(int manualDegrees, bool autoRotate, PixelSize nativePixelSize)
    {
        int autoContribution = autoRotate && nativePixelSize.Width > nativePixelSize.Height ? -90 : 0;
        return ((manualDegrees + autoContribution) % 360 + 360) % 360;
    }

    public static (double X, double Y) ClampPan(Size canvasSize, PixelSize imagePixelSize, double zoom, double proposedX, double proposedY, ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false)
    {
        double baseScale = ComputeBaseScale(canvasSize, imagePixelSize, fitMode, fitOnlyIfOversized);
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
    public static (double X, double Y) PanToCenterOn(Size canvasSize, PixelSize imagePixelSize, double targetZoom, Point clickPoint, ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false)
    {
        double baseScale = ComputeBaseScale(canvasSize, imagePixelSize, fitMode, fitOnlyIfOversized);
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

        return ClampPan(canvasSize, imagePixelSize, targetZoom, panX, panY, fitMode, fitOnlyIfOversized);
    }

    /// <summary>
    /// Ctrl+wheel/pinch: the image point currently under <paramref name="cursorPoint"/> stays under
    /// the cursor as zoom changes from <paramref name="currentZoom"/>/<paramref name="currentPan"/>
    /// to <paramref name="targetZoom"/> - works incrementally from any current state (unlike
    /// <see cref="PanToCenterOn"/>), which is what makes wheel-zoom feel anchored instead of having
    /// the image drift under the cursor.
    /// </summary>
    public static (double X, double Y) PanToKeepPointFixed(Size canvasSize, PixelSize imagePixelSize, double currentZoom, Point currentPan, Point cursorPoint, double targetZoom, ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false)
    {
        double baseScale = ComputeBaseScale(canvasSize, imagePixelSize, fitMode, fitOnlyIfOversized);
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

        return ClampPan(canvasSize, imagePixelSize, targetZoom, panX1, panY1, fitMode, fitOnlyIfOversized);
    }
}
