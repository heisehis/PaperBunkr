using Avalonia;
using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Panel-to-view geometry, stepping rules and the tween (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md sections 3 and 4).</summary>
public class PanelViewMathTests
{
    private static readonly Size Viewport = new(1000, 800);
    private static readonly PixelSize Page = new(800, 1200);   // fits by height: base scale 800/1200

    // --- ForPanel ---

    [Fact]
    public void ForPanel_WholePage_IsTheFitView()
    {
        var (zoom, panX, panY) = PanelViewMath.ForPanel(PanelRect.WholePage, Page, Viewport);

        Assert.Equal(1.0, zoom, 6);
        Assert.Equal(0, panX, 6);
        Assert.Equal(0, panY, 6);
    }

    [Fact]
    public void ForPanel_CentredPanel_IsCentredExactly()
    {
        var panel = new PanelRect(0.3, 0.35, 0.4, 0.3);   // centre (0.5, 0.5)

        var (zoom, panX, panY) = PanelViewMath.ForPanel(panel, Page, Viewport);

        Assert.True(zoom > 1.5);
        Assert.Equal(0, panX, 6);
        Assert.Equal(0, panY, 6);
    }

    [Fact]
    public void ForPanel_UpperLeftPanel_ZoomsIn_AndIsFullyVisible()
    {
        var panel = new PanelRect(0.05, 0.05, 0.4, 0.4);

        var (zoom, panX, panY) = PanelViewMath.ForPanel(panel, Page, Viewport);

        Assert.True(zoom > 1.5);
        double baseScale = ZoomPanMath.ComputeBaseScale(Viewport, Page);
        double displayedW = Page.Width * baseScale * zoom;
        double displayedH = Page.Height * baseScale * zoom;
        double left = ((Viewport.Width - displayedW) / 2) + panX;
        double top = ((Viewport.Height - displayedH) / 2) + panY;
        Assert.True(left + (panel.X * displayedW) >= -0.001);
        Assert.True(left + (panel.Right * displayedW) <= Viewport.Width + 0.001);
        Assert.True(top + (panel.Y * displayedH) >= -0.001);
        Assert.True(top + (panel.Bottom * displayedH) <= Viewport.Height + 0.001);
    }

    [Fact]
    public void ForPanel_LeavesAFourPercentMargin()
    {
        var panel = new PanelRect(0.25, 0.25, 0.5, 0.25);

        var (zoom, _, _) = PanelViewMath.ForPanel(panel, Page, Viewport);

        double baseScale = ZoomPanMath.ComputeBaseScale(Viewport, Page);
        double shownW = panel.Width * Page.Width * baseScale * zoom;
        double shownH = panel.Height * Page.Height * baseScale * zoom;
        Assert.True(shownW <= Viewport.Width * (1 - (2 * PanelViewMath.Margin)) + 0.001);
        Assert.True(shownH <= Viewport.Height * (1 - (2 * PanelViewMath.Margin)) + 0.001);
        // And the limiting side actually fills that room.
        Assert.True(Math.Abs(shownW - (Viewport.Width * 0.92)) < 0.01 || Math.Abs(shownH - (Viewport.Height * 0.92)) < 0.01 || zoom == ZoomPanMath.MaxZoom);
    }

    [Fact]
    public void ForPanel_TinyPanel_IsCappedAtFourHundredPercent()
    {
        var (zoom, _, _) = PanelViewMath.ForPanel(new PanelRect(0.4, 0.4, 0.05, 0.05), Page, Viewport);

        Assert.Equal(ZoomPanMath.MaxZoom, zoom);
    }

    [Fact]
    public void ForPanel_NeverZoomsOutBelowFit()
    {
        // A "panel" bigger than the page can't be requested, but a full-page one must give exactly fit.
        var (zoom, _, _) = PanelViewMath.ForPanel(new PanelRect(0, 0, 1, 1), Page, Viewport);

        Assert.True(zoom >= ZoomPanMath.FitZoom);
    }

    [Fact]
    public void ForPanel_PanIsClamped_SoTheViewNeverLeavesThePage()
    {
        // A panel in the very corner cannot be centred without showing space beyond the page; the pan stops at the edge.
        var panel = new PanelRect(0, 0, 0.3, 0.3);

        var (zoom, panX, panY) = PanelViewMath.ForPanel(panel, Page, Viewport);
        var (clampedX, clampedY) = ZoomPanMath.ClampPan(Viewport, Page, zoom, panX, panY);

        Assert.Equal(clampedX, panX, 6);
        Assert.Equal(clampedY, panY, 6);
    }

    [Fact]
    public void ForPanel_DegenerateInputs_GiveTheFitView()
    {
        Assert.Equal((1.0, 0.0, 0.0), PanelViewMath.ForPanel(new PanelRect(0, 0, 0, 0.5), Page, Viewport));
        Assert.Equal((1.0, 0.0, 0.0), PanelViewMath.ForPanel(PanelRect.WholePage, Page, new Size(0, 0)));
    }

    // --- Rotation ---

    [Theory]
    [InlineData(0, 0.1, 0.2, 0.3, 0.4, 0.1, 0.2, 0.3, 0.4)]
    [InlineData(90, 0.1, 0.2, 0.3, 0.4, 0.4, 0.1, 0.4, 0.3)]
    [InlineData(180, 0.1, 0.2, 0.3, 0.4, 0.6, 0.4, 0.3, 0.4)]
    [InlineData(270, 0.1, 0.2, 0.3, 0.4, 0.2, 0.6, 0.4, 0.3)]
    [InlineData(-90, 0.1, 0.2, 0.3, 0.4, 0.2, 0.6, 0.4, 0.3)]
    [InlineData(450, 0.1, 0.2, 0.3, 0.4, 0.4, 0.1, 0.4, 0.3)]
    public void RotateRect_MapsAClockwiseQuarterTurns(int degrees, double x, double y, double w, double h, double ex, double ey, double ew, double eh)
    {
        var r = PanelViewMath.RotateRect(new PanelRect(x, y, w, h), degrees);

        Assert.Equal(ex, r.X, 9);
        Assert.Equal(ey, r.Y, 9);
        Assert.Equal(ew, r.Width, 9);
        Assert.Equal(eh, r.Height, 9);
    }

    [Fact]
    public void RotateRect_FourQuarterTurns_ComeBack()
    {
        var r = new PanelRect(0.12, 0.34, 0.2, 0.3);
        var turned = r;
        for (int i = 0; i < 4; i++)
        {
            turned = PanelViewMath.RotateRect(turned, 90);
        }

        Assert.Equal(r.X, turned.X, 9);
        Assert.Equal(r.Y, turned.Y, 9);
        Assert.Equal(r.Width, turned.Width, 9);
        Assert.Equal(r.Height, turned.Height, 9);
    }

    // --- Hit testing ---

    [Fact]
    public void PagePointAt_FitView_MapsTheCanvasCentreToThePageCentre()
    {
        var point = PanelViewMath.PagePointAt(new Point(500, 400), Viewport, Page, 1.0, 0, 0);

        Assert.NotNull(point);
        Assert.Equal(0.5, point!.Value.X, 6);
        Assert.Equal(0.5, point.Value.Y, 6);
    }

    [Fact]
    public void PagePointAt_OutsideThePage_IsNull()
    {
        // Fit view: the page is 533 px wide, centred in 1000, so x = 10 is beside it.
        Assert.Null(PanelViewMath.PagePointAt(new Point(10, 400), Viewport, Page, 1.0, 0, 0));
    }

    [Fact]
    public void CurrentPanelIndex_FollowsWhatIsCentred()
    {
        var panels = new[] { new PanelRect(0.05, 0.05, 0.4, 0.4), new PanelRect(0.55, 0.05, 0.4, 0.4) };
        var (zoom, panX, panY) = PanelViewMath.ForPanel(panels[1], Page, Viewport);

        Assert.Equal(1, PanelViewMath.CurrentPanelIndex(panels, Viewport, Page, zoom, panX, panY));
        Assert.Equal(-1, PanelViewMath.CurrentPanelIndex([], Viewport, Page, 1, 0, 0));
    }

    // --- Stepping ---

    [Theory]
    [InlineData(4, 0, true, PanelStepOutcome.Moved, 1)]
    [InlineData(4, 2, true, PanelStepOutcome.Moved, 3)]
    [InlineData(4, 3, true, PanelStepOutcome.PastEnd, 3)]
    [InlineData(4, 3, false, PanelStepOutcome.Moved, 2)]
    [InlineData(4, 0, false, PanelStepOutcome.PastStart, 0)]
    [InlineData(1, 0, true, PanelStepOutcome.PastEnd, 0)]
    [InlineData(1, 0, false, PanelStepOutcome.PastStart, 0)]
    [InlineData(4, -1, true, PanelStepOutcome.Moved, 0)]
    [InlineData(4, -1, false, PanelStepOutcome.Moved, 3)]
    [InlineData(0, -1, true, PanelStepOutcome.PastEnd, -1)]
    [InlineData(0, -1, false, PanelStepOutcome.PastStart, -1)]
    public void Step_MovesWithinAPage_AndReportsWhenItRunsOut(int count, int current, bool forward, PanelStepOutcome outcome, int index)
    {
        var (o, i) = PanelStepper.Step(count, current, forward);

        Assert.Equal(outcome, o);
        Assert.Equal(index, i);
    }

    [Theory]
    [InlineData(5, true, 0)]
    [InlineData(5, false, 4)]
    [InlineData(0, true, -1)]
    [InlineData(0, false, -1)]
    public void Landing_IsTheFirstPanelGoingForward_AndTheLastGoingBack(int count, bool forward, int expected) =>
        Assert.Equal(expected, PanelStepper.Landing(count, forward));

    [Fact]
    public void IndexAt_And_ContainingIndex()
    {
        var panels = new[] { new PanelRect(0.05, 0.05, 0.4, 0.4), new PanelRect(0.55, 0.05, 0.4, 0.4) };

        Assert.Equal(0, PanelStepper.IndexAt(panels, 0.2, 0.2));
        Assert.Equal(0, PanelStepper.IndexAt(panels, 0.5, 0.2));          // the gutter is equally near both centres, the first one found wins
        Assert.Equal(-1, PanelStepper.ContainingIndex(panels, 0.5, 0.2));  // strictly containing: the gutter finds nothing
        Assert.Equal(1, PanelStepper.ContainingIndex(panels, 0.7, 0.3));
        Assert.Equal(-1, PanelStepper.IndexAt([], 0.5, 0.5));
    }

    [Theory]
    [InlineData(2, 7, "Panel 3/7")]
    [InlineData(0, 1, "Panel 1/1")]
    [InlineData(-1, 7, "")]
    [InlineData(0, 0, "")]
    public void Label(int index, int count, string expected) => Assert.Equal(expected, PanelStepper.Label(index, count));

    // --- Tween ---

    [Fact]
    public void Tween_StartsAtFrom_EndsAtTo_AndIsCompleteAfterTheDuration()
    {
        var tween = new ZoomPanTween(1, 0, 0, 3, 120, -80, 180);

        Assert.Equal((1.0, 0.0, 0.0), tween.Evaluate(0));
        Assert.Equal((3.0, 120.0, -80.0), tween.Evaluate(180));
        Assert.Equal((3.0, 120.0, -80.0), tween.Evaluate(500));
        Assert.False(tween.IsComplete(179));
        Assert.True(tween.IsComplete(180));
    }

    [Fact]
    public void Tween_IsMonotonic_AndEasesOut()
    {
        var tween = new ZoomPanTween(1, 0, 0, 4, 100, 0, 180);
        double previousZoom = 1;
        double previousPan = 0;
        for (int ms = 10; ms <= 170; ms += 10)
        {
            var (zoom, pan, _) = tween.Evaluate(ms);
            Assert.True(zoom >= previousZoom);
            Assert.True(pan >= previousPan);
            previousZoom = zoom;
            previousPan = pan;
        }

        // Ease-out: more than half the pan is done by the halfway time.
        Assert.True(tween.Evaluate(90).PanX > 50);
    }

    [Fact]
    public void Tween_InterpolatesZoomInLogSpace()
    {
        var tween = new ZoomPanTween(1, 0, 0, 4, 0, 0, 200);

        // At the eased midpoint (t such that ease = 0.5) zoom is the geometric, not arithmetic, mean.
        double zoomAtHalfEase = 1 * Math.Pow(4, 0.5);
        Assert.Equal(2.0, zoomAtHalfEase, 9);
        double t = 1 - Math.Pow(0.5, 1.0 / 3.0);   // solves 1 - (1-t)^3 = 0.5
        Assert.Equal(2.0, tween.Evaluate(t * 200).Zoom, 6);
    }

    [Fact]
    public void Tween_ZeroDuration_JumpsStraightToTheEnd()
    {
        var tween = new ZoomPanTween(1, 0, 0, 2, 5, 5, 0);

        Assert.Equal((2.0, 5.0, 5.0), tween.Evaluate(0));
        Assert.True(tween.IsComplete(0));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-3, 0)]
    [InlineData(4, 1)]
    public void EaseOutCubic_IsClampedAtTheEnds(double t, double expected) => Assert.Equal(expected, ZoomPanTween.EaseOutCubic(t), 9);

    // --- SplitOversized: panels bigger than the screen at 100% are cut into screen-sized slices ---

    [Fact]
    public void SplitOversized_PanelsThatFit_AreReturnedUntouched()
    {
        var panels = new[] { new PanelRect(0.05, 0.05, 0.4, 0.4), new PanelRect(0.5, 0.5, 0.4, 0.4) };

        var result = PanelViewMath.SplitOversized(panels, Page, Viewport, ImageFitMode.Fit, false, false);

        Assert.Same(panels, result);
    }

    [Fact]
    public void SplitOversized_TallPanelInFitWidth_BecomesOverlappingScreenSizedSlices_TopToBottom()
    {
        // A long strip, fit to width: 800x12000 at base scale 1.25 is 15000 px tall in a 1000x800 viewport.
        var strip = new PixelSize(800, 12000);
        var panel = new PanelRect(0, 0.2, 1, 0.3);            // 4500 px tall on screen

        var slices = PanelViewMath.SplitOversized([panel], strip, Viewport, ImageFitMode.FitWidth, false, false);

        Assert.True(slices.Count >= 6, $"{slices.Count} slices");
        double baseScale = ZoomPanMath.ComputeBaseScale(Viewport, strip, ImageFitMode.FitWidth);
        for (int i = 0; i < slices.Count; i++)
        {
            Assert.InRange(slices[i].Height * strip.Height * baseScale, 0, Viewport.Height * 0.92 * 1.03);   // each fits the viewport
            Assert.InRange(slices[i].Y, panel.Y - 1e-9, panel.Y + panel.Height + 1e-9);
            if (i > 0)
            {
                Assert.True(slices[i].Y > slices[i - 1].Y);                                                 // reading order: down the panel
                Assert.True(slices[i].Y < slices[i - 1].Y + slices[i - 1].Height);                          // and they overlap
            }
        }

        Assert.Equal(panel.Y, slices[0].Y, 9);                                                              // first slice starts at the top, last ends at the bottom
        Assert.Equal(panel.Y + panel.Height, slices[^1].Y + slices[^1].Height, 9);
    }

    [Fact]
    public void SplitOversized_WidePanel_SplitsLeftToRight_OrRightToLeft()
    {
        var wide = new PixelSize(3000, 800);                    // fit-height: 1.0 scale, 3000 px wide
        var panel = new PanelRect(0, 0, 1, 1);

        var ltr = PanelViewMath.SplitOversized([panel], wide, Viewport, ImageFitMode.FitHeight, false, false);
        var rtl = PanelViewMath.SplitOversized([panel], wide, Viewport, ImageFitMode.FitHeight, false, true);

        Assert.True(ltr.Count >= 3);
        Assert.Equal(ltr.Count, rtl.Count);
        Assert.True(ltr[0].X < ltr[^1].X);
        Assert.True(rtl[0].X > rtl[^1].X);
    }

    [Fact]
    public void SplitOversized_FitMode_NeverSplits_BecauseEveryPanelFitsTheViewport()
    {
        var panels = new[] { PanelRect.WholePage };

        Assert.Same(panels, PanelViewMath.SplitOversized(panels, new PixelSize(800, 15000), Viewport, ImageFitMode.Fit, false, false));
    }

    [Fact]
    public void ForPanel_ASlice_IsShownAtNoMoreThanItsOwnZoom_AndCentredOnIt()
    {
        var strip = new PixelSize(800, 12000);
        var slices = PanelViewMath.SplitOversized([new PanelRect(0, 0, 1, 1)], strip, Viewport, ImageFitMode.FitWidth, false, false);

        var (zoom, _, panY) = PanelViewMath.ForPanel(slices[2], strip, Viewport, ImageFitMode.FitWidth);

        Assert.InRange(zoom, 1.0, 1.1);                          // a slice fits at 100%: no extra zoom
        double baseScale = ZoomPanMath.ComputeBaseScale(Viewport, strip, ImageFitMode.FitWidth);
        double displayedH = strip.Height * baseScale * zoom;
        Assert.Equal(displayedH * (0.5 - slices[2].CenterY), panY, 3);
    }

    // --- The zoom glide ---

    [Fact]
    public void StepZoomTowards_MovesPartWayInLogSpace_AndNeverOvershoots()
    {
        double zoom = 1.0;
        double previous = zoom;
        for (int frame = 0; frame < 200; frame++)
        {
            zoom = PageCanvas.StepZoomTowards(zoom, 2.0, 1.0 / 60);
            Assert.InRange(zoom, previous, 2.0);
            previous = zoom;
        }

        Assert.Equal(2.0, zoom, 9);                              // snapped onto the goal
    }

    [Fact]
    public void StepZoomTowards_OneFrame_CoversAShareOfTheDistance_ZoomingInAndOutAlike()
    {
        double zoomIn = PageCanvas.StepZoomTowards(1.0, 2.0, 1.0 / 60);
        double zoomOut = PageCanvas.StepZoomTowards(2.0, 1.0, 1.0 / 60);

        Assert.InRange(zoomIn, 1.05, 1.5);
        Assert.Equal(Math.Log(zoomIn) / Math.Log(2.0), (Math.Log(2.0) - Math.Log(zoomOut)) / Math.Log(2.0), 9);   // the same share of the log distance
    }

    [Fact]
    public void StepZoomTowards_ALongPause_LandsNearTheGoal_ButNotPastIt()
    {
        double zoom = PageCanvas.StepZoomTowards(1.0, 4.0, 5.0);

        Assert.InRange(zoom, 3.99, 4.0);
    }
}
