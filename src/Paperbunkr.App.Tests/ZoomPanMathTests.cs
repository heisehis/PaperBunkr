using Avalonia;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

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

    // Fit-mode coverage (docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-controls-
    // design.md §2) - canvas 400x300, image 800x300 (2x wide, 1x tall): widthRatio=0.5,
    // heightRatio=1.0, so Fit/BestFit's min/max pick genuinely different values, not the same one
    // by coincidence.
    [Fact]
    public void ComputeBaseScale_Original_AlwaysReturnsOne()
    {
        Assert.Equal(1.0, ZoomPanMath.ComputeBaseScale(new Size(400, 300), new PixelSize(800, 300), ImageFitMode.Original));
        Assert.Equal(1.0, ZoomPanMath.ComputeBaseScale(new Size(400, 300), new PixelSize(100, 50), ImageFitMode.Original, fitOnlyIfOversized: true));
    }

    [Fact]
    public void ComputeBaseScale_Fit_ReturnsMinRatio_MatchingPreExistingDefaultBehavior()
    {
        // No fitMode argument at all - confirms the parameterless call sites elsewhere in this file
        // (predating this design pass) still get exactly today's behavior.
        Assert.Equal(0.5, ZoomPanMath.ComputeBaseScale(new Size(400, 300), new PixelSize(800, 300)));
        Assert.Equal(0.5, ZoomPanMath.ComputeBaseScale(new Size(400, 300), new PixelSize(800, 300), ImageFitMode.Fit));
    }

    [Fact]
    public void ComputeBaseScale_BestFit_ReturnsMaxRatio_OppositeOfFit()
    {
        Assert.Equal(1.0, ZoomPanMath.ComputeBaseScale(new Size(400, 300), new PixelSize(800, 300), ImageFitMode.BestFit));
    }

    [Fact]
    public void ComputeBaseScale_FitWidthAndFitHeight_UseOnlyTheirOwnDimension()
    {
        // Canvas 400x300, image 800x900: widthRatio=0.5, heightRatio=0.333... - deliberately
        // different values so a swapped implementation would fail loudly.
        Assert.Equal(0.5, ZoomPanMath.ComputeBaseScale(new Size(400, 300), new PixelSize(800, 900), ImageFitMode.FitWidth));
        Assert.Equal(300.0 / 900.0, ZoomPanMath.ComputeBaseScale(new Size(400, 300), new PixelSize(800, 900), ImageFitMode.FitHeight), precision: 6);
    }

    [Fact]
    public void ComputeBaseScale_FitOnlyIfOversized_FitAndBestFit_SkipAsymmetrically()
    {
        // Canvas 400x300, image 200x600: canvas is already wider than the image (widthFits=true)
        // but shorter than it (heightFits=false). Confirmed from ComicRackCE source
        // (ImageDisplayControl.GetScale) that Fit only skips fitting when BOTH dimensions already
        // fit (&&), while BestFit skips if EITHER already fits (||) - a real asymmetry, not a typo.
        var canvas = new Size(400, 300);
        var image = new PixelSize(200, 600);

        double fitScale = ZoomPanMath.ComputeBaseScale(canvas, image, ImageFitMode.Fit, fitOnlyIfOversized: true);
        double bestFitScale = ZoomPanMath.ComputeBaseScale(canvas, image, ImageFitMode.BestFit, fitOnlyIfOversized: true);

        Assert.Equal(0.5, fitScale, precision: 6); // didn't skip - both dimensions must fit, and height doesn't
        Assert.Equal(1.0, bestFitScale, precision: 6); // skipped - width alone already fits
    }

    [Fact]
    public void ComputeBaseScale_FitWidth_FitOnlyIfOversized_SkipsWhenWidthAlreadyFits()
    {
        // Canvas wider than the image (400 > 100) - FitWidth would otherwise upscale.
        Assert.Equal(1.0, ZoomPanMath.ComputeBaseScale(new Size(400, 300), new PixelSize(100, 50), ImageFitMode.FitWidth, fitOnlyIfOversized: true));
    }

    [Fact]
    public void ComputeBaseScale_FitHeight_FitOnlyIfOversized_SkipsWhenHeightAlreadyFits()
    {
        Assert.Equal(1.0, ZoomPanMath.ComputeBaseScale(new Size(400, 300), new PixelSize(100, 50), ImageFitMode.FitHeight, fitOnlyIfOversized: true));
    }

    // HasOverflow (pan-enable gate) - real bug found via manual testing: Original/FitWidth/
    // FitHeight/BestFit can all make content bigger than the canvas at zoom == MinZoom, unlike the
    // pre-fit-mode-era "Fit always contains" assumption ZoomLevel-only pan gating relied on.
    [Fact]
    public void HasOverflow_Original_NativeSizeBiggerThanCanvas_ReturnsTrue()
    {
        // Canvas 400x300, image at its own native 800x600 (Original ignores the canvas entirely).
        Assert.True(ZoomPanMath.HasOverflow(new Size(400, 300), new PixelSize(800, 600), zoom: 1.0, ImageFitMode.Original));
    }

    [Fact]
    public void HasOverflow_Original_NativeSizeSmallerThanCanvas_ReturnsFalse()
    {
        Assert.False(ZoomPanMath.HasOverflow(new Size(400, 300), new PixelSize(100, 50), zoom: 1.0, ImageFitMode.Original));
    }

    [Fact]
    public void HasOverflow_FitWidth_TallImage_OverflowsVertically()
    {
        // Canvas 400x300, image 800x1600 (very tall) - FitWidth scales to 400 wide, 800 tall: fits
        // width exactly, overflows height.
        Assert.True(ZoomPanMath.HasOverflow(new Size(400, 300), new PixelSize(800, 1600), zoom: 1.0, ImageFitMode.FitWidth));
    }

    [Fact]
    public void HasOverflow_Fit_NeverOverflows_RegardlessOfAspectRatio()
    {
        // Fit (contain) is defined to never overflow either dimension - the one mode pan gating
        // could previously assume "no zoom means no overflow" for.
        Assert.False(ZoomPanMath.HasOverflow(new Size(400, 300), new PixelSize(800, 1600), zoom: 1.0, ImageFitMode.Fit));
        Assert.False(ZoomPanMath.HasOverflow(new Size(400, 300), new PixelSize(1600, 300), zoom: 1.0, ImageFitMode.Fit));
    }

    [Fact]
    public void HasOverflow_BestFit_MismatchedAspectRatio_AlwaysOverflowsOneDimension()
    {
        // BestFit (cover) is defined to always fill/overflow unless the aspect ratio matches exactly.
        Assert.True(ZoomPanMath.HasOverflow(new Size(400, 300), new PixelSize(800, 1600), zoom: 1.0, ImageFitMode.BestFit));
    }

    [Fact]
    public void HasOverflow_FitPlusUserZoom_Overflows()
    {
        // Fit alone never overflows, but zooming in past 1.0 always does - the pre-existing case
        // this gate already handled correctly before fit modes existed.
        Assert.True(ZoomPanMath.HasOverflow(new Size(400, 300), new PixelSize(800, 600), zoom: 2.0, ImageFitMode.Fit));
    }

    // Auto-rotate trigger (spec §4) - ported from ComicRackCE: fires only when the *native*
    // (unrotated) bitmap is landscape, composed with manual rotation rather than replacing it.
    [Fact]
    public void ComposeRotationDegrees_LandscapeImageWithAutoRotateOn_SubtractsNinety()
    {
        Assert.Equal(270, ZoomPanMath.ComposeRotationDegrees(0, autoRotate: true, new PixelSize(800, 600)));
    }

    [Fact]
    public void ComposeRotationDegrees_PortraitImageWithAutoRotateOn_NoContribution()
    {
        Assert.Equal(90, ZoomPanMath.ComposeRotationDegrees(90, autoRotate: true, new PixelSize(600, 800)));
    }

    [Fact]
    public void ComposeRotationDegrees_AutoRotateOff_NeverContributesRegardlessOfShape()
    {
        Assert.Equal(0, ZoomPanMath.ComposeRotationDegrees(0, autoRotate: false, new PixelSize(800, 600)));
    }

    [Fact]
    public void ComposeRotationDegrees_ComposesWithManualRotation_NotReplacingIt()
    {
        // Manual 180 + auto-rotate's -90 on a landscape image = 90, not -90 - proves composition,
        // not override.
        Assert.Equal(90, ZoomPanMath.ComposeRotationDegrees(180, autoRotate: true, new PixelSize(800, 600)));
    }

    [Fact]
    public void ComposeRotationDegrees_AlwaysWrapsToPositiveZeroTo360Range()
    {
        int result = ZoomPanMath.ComposeRotationDegrees(0, autoRotate: true, new PixelSize(800, 600));
        Assert.InRange(result, 0, 359);
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
