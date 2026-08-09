using Avalonia;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ZoomPanMath"/>'s pure geometry (docs/superpowers/specs/
/// 2026-08-09-reader-gestures-and-grid-navigation-design.md §B1). Plain <see cref="Size"/>/
/// <see cref="PixelSize"/>/<see cref="Point"/>/<see langword="double"/> inputs - no Avalonia app
/// context needed.
/// </summary>
public class ZoomPanMathTests
{
    [Fact]
    public void ClampZoom_BelowMin_ClampsToMin() => Assert.Equal(ZoomPanMath.MinZoom, ZoomPanMath.ClampZoom(0.5));

    [Fact]
    public void ClampZoom_AboveMax_ClampsToMax() => Assert.Equal(ZoomPanMath.MaxZoom, ZoomPanMath.ClampZoom(10));

    [Fact]
    public void ClampZoom_WithinRange_Unchanged() => Assert.Equal(2.5, ZoomPanMath.ClampZoom(2.5));

    [Fact]
    public void ClampPan_AtMinZoom_AlwaysZero_RegardlessOfProposedOffset()
    {
        var (x, y) = ZoomPanMath.ClampPan(new Size(400, 300), new PixelSize(400, 300), ZoomPanMath.MinZoom, 500, -500);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void ClampPan_SquareAspectMatch_ClampsSymmetrically()
    {
        // Canvas and image both 400x400 (matched aspect ratio) so baseScale is 1 on both axes -
        // maxPan comes out identical on X and Y, proving the formula treats both axes uniformly
        // when there's no letterboxing to break the symmetry.
        var (x, y) = ZoomPanMath.ClampPan(new Size(400, 400), new PixelSize(400, 400), zoom: 2, proposedX: 500, proposedY: -500);

        Assert.Equal(200, x);
        Assert.Equal(-200, y);
    }

    [Fact]
    public void ClampPan_LetterboxedAxis_StaysZeroEvenWhenOtherAxisOverflows()
    {
        // Canvas 400x300, image 200x300 (portrait): baseScale=1, so at any zoom>1 the image's
        // height already fills the canvas exactly (overflows immediately) while its width has
        // slack (doesn't overflow until zoom>2). At zoom=1.5, Y has already overflowed but X's
        // letterbox margin means it hasn't - proving the two axes clamp independently.
        var (x, y) = ZoomPanMath.ClampPan(new Size(400, 300), new PixelSize(200, 300), zoom: 1.5, proposedX: 999, proposedY: 999);

        Assert.Equal(0, x);
        Assert.Equal(75, y, precision: 6);
    }

    [Fact]
    public void ComputeBaseScale_DegenerateInputs_ReturnsZero()
    {
        Assert.Equal(0, ZoomPanMath.ComputeBaseScale(new Size(0, 300), new PixelSize(400, 300)));
        Assert.Equal(0, ZoomPanMath.ComputeBaseScale(new Size(400, 300), new PixelSize(0, 300)));
    }

    [Fact]
    public void PanToCenterOn_ClickAtImageCenter_ReturnsZeroPan()
    {
        var (x, y) = ZoomPanMath.PanToCenterOn(new Size(400, 300), new PixelSize(400, 300), targetZoom: 2, clickPoint: new Point(200, 150));

        Assert.Equal(0, x, precision: 6);
        Assert.Equal(0, y, precision: 6);
    }

    [Fact]
    public void PanToCenterOn_ClickNearImageCorner_ClampedByMaxPan()
    {
        var (x, y) = ZoomPanMath.PanToCenterOn(new Size(400, 300), new PixelSize(400, 300), targetZoom: 2, clickPoint: new Point(10, 10));

        // Recentering near the top-left corner wants far more pan than zoom=2 allows (maxPanX=200,
        // maxPanY=150 for this canvas/image) - the result saturates at the max in both directions.
        Assert.Equal(200, x, precision: 6);
        Assert.Equal(150, y, precision: 6);
    }

    [Fact]
    public void PanToCenterOn_ClickOutsideLetterboxedImage_FractionClampsTo0Or1()
    {
        // Canvas 400x300, image 200x300 (portrait, pillarboxed): image occupies x in [100,300] at
        // baseScale. Clicking miles outside that (and even outside the canvas) must clamp the
        // click fraction to [0,1] rather than extrapolating - the point-under-click math should
        // treat this exactly like clicking on the image's edge, not blow up.
        var (x, y) = ZoomPanMath.PanToCenterOn(new Size(400, 300), new PixelSize(200, 300), targetZoom: 2, clickPoint: new Point(-500, 1000));

        Assert.Equal(0, x, precision: 6);
        Assert.Equal(-150, y, precision: 6);
    }

    [Fact]
    public void PanToKeepPointFixed_ZoomingIn_CursorPointMapsToSameScreenPosition()
    {
        var canvasSize = new Size(400, 300);
        var imageSize = new PixelSize(400, 300);
        var cursor = new Point(300, 200);

        double uBefore = ComputeCursorFraction(canvasSize, imageSize, zoom: 1, pan: new Point(0, 0), cursor).X;
        double vBefore = ComputeCursorFraction(canvasSize, imageSize, zoom: 1, pan: new Point(0, 0), cursor).Y;

        var (panX, panY) = ZoomPanMath.PanToKeepPointFixed(canvasSize, imageSize, currentZoom: 1, currentPan: new Point(0, 0), cursorPoint: cursor, targetZoom: 2);

        var after = ComputeCursorFraction(canvasSize, imageSize, zoom: 2, pan: new Point(panX, panY), cursor);

        Assert.Equal(uBefore, after.X, precision: 6);
        Assert.Equal(vBefore, after.Y, precision: 6);
    }

    [Fact]
    public void PanToKeepPointFixed_ResultIsAlreadyClamped_WhenAnchorNearImageEdge()
    {
        var canvasSize = new Size(400, 300);
        var imageSize = new PixelSize(400, 300);

        var (x, y) = ZoomPanMath.PanToKeepPointFixed(canvasSize, imageSize, currentZoom: 1, currentPan: new Point(0, 0), cursorPoint: new Point(0, 0), targetZoom: 4);

        var (maxX, maxY) = ZoomPanMath.ClampPan(canvasSize, imageSize, zoom: 4, proposedX: double.MaxValue / 2, proposedY: double.MaxValue / 2);
        Assert.Equal(maxX, x, precision: 6);
        Assert.Equal(maxY, y, precision: 6);
    }

    [Fact]
    public void PanToKeepPointFixed_FromAlreadyPannedState_StillAnchorsCorrectly()
    {
        var canvasSize = new Size(400, 300);
        var imageSize = new PixelSize(400, 300);
        var startPan = new Point(50, 30);
        var cursor = new Point(250, 180);

        var before = ComputeCursorFraction(canvasSize, imageSize, zoom: 2, pan: startPan, cursor);

        var (panX, panY) = ZoomPanMath.PanToKeepPointFixed(canvasSize, imageSize, currentZoom: 2, currentPan: startPan, cursorPoint: cursor, targetZoom: 3);

        var after = ComputeCursorFraction(canvasSize, imageSize, zoom: 3, pan: new Point(panX, panY), cursor);

        Assert.Equal(before.X, after.X, precision: 6);
        Assert.Equal(before.Y, after.Y, precision: 6);
    }

    /// <summary>
    /// Re-derives the fractional image-space point under <paramref name="cursor"/> from the same
    /// forward placement formula <see cref="ZoomPanMath.PanToKeepPointFixed"/> anchors against -
    /// lets the "point under cursor doesn't move" tests assert on behavior instead of a hardcoded,
    /// fragile expected pan value.
    /// </summary>
    private static Point ComputeCursorFraction(Size canvasSize, PixelSize imageSize, double zoom, Point pan, Point cursor)
    {
        double baseScale = ZoomPanMath.ComputeBaseScale(canvasSize, imageSize);
        double displayedW = imageSize.Width * baseScale * zoom;
        double displayedH = imageSize.Height * baseScale * zoom;
        double imageLeft = ((canvasSize.Width - displayedW) / 2) + pan.X;
        double imageTop = ((canvasSize.Height - displayedH) / 2) + pan.Y;

        return new Point((cursor.X - imageLeft) / displayedW, (cursor.Y - imageTop) / displayedH);
    }
}
