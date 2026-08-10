using Avalonia;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReaderLayoutModel"/>'s pure geometry, same "plain Avalonia value types, no
/// app context needed" shape as <see cref="ZoomPanMathTests"/>.
/// </summary>
public class ReaderLayoutModelTests
{
    [Fact]
    public void ComputeSinglePageLayout_ReturnsOnePage_FillingViewport()
    {
        var pages = ReaderLayoutModel.ComputeSinglePageLayout(pageIndex: 3, new Size(800, 600));

        var page = Assert.Single(pages);
        Assert.Equal(3, page.Index);
        Assert.Equal(new Rect(0, 0, 800, 600), page.Rect);
    }

    [Fact]
    public void ComputeContinuousLayout_Vertical_FitsEachPageToViewportWidth()
    {
        // Two pages, both wider than the viewport, different aspect ratios - each should scale
        // independently to exactly fill the 400px-wide viewport.
        var sizes = new[] { new Size(800, 1200), new Size(400, 400) };

        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(400, 600), ReaderLayoutModel.Axis.Vertical, virtualizationRadius: 1);

        Assert.Equal(2, pages.Count);
        Assert.Equal(400, pages[0].Rect.Width);
        Assert.Equal(600, pages[0].Rect.Height); // 1200 * (400/800)
        Assert.Equal(400, pages[1].Rect.Width);
        Assert.Equal(400, pages[1].Rect.Height); // 400 * (400/400)
    }

    [Fact]
    public void ComputeContinuousLayout_Vertical_StacksPagesByCumulativeHeight()
    {
        var sizes = new[] { new Size(400, 600), new Size(400, 300), new Size(400, 900) };

        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(400, 100), ReaderLayoutModel.Axis.Vertical, virtualizationRadius: 5);

        Assert.Equal(0, pages[0].Rect.Y);
        Assert.Equal(600, pages[1].Rect.Y); // after page 0's 600px
        Assert.Equal(900, pages[2].Rect.Y); // after page 0 (600) + page 1 (300)
    }

    [Fact]
    public void ComputeContinuousLayout_Vertical_ScrollOffsetShiftsRectsUpward()
    {
        var sizes = new[] { new Size(400, 600), new Size(400, 600) };

        // Scrolled 200px into the stack - page 0's top should now be at -200 (above the viewport).
        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 200, new Size(400, 300), ReaderLayoutModel.Axis.Vertical, virtualizationRadius: 5);

        var page0 = pages.Single(p => p.Index == 0);
        Assert.Equal(-200, page0.Rect.Y);
    }

    [Fact]
    public void ComputeContinuousLayout_Horizontal_FitsEachPageToViewportHeight_AndStacksByWidth()
    {
        var sizes = new[] { new Size(800, 400), new Size(400, 400) };

        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(100, 200), ReaderLayoutModel.Axis.Horizontal, virtualizationRadius: 5);

        Assert.Equal(200, pages[0].Rect.Height); // fit to viewport height
        Assert.Equal(400, pages[0].Rect.Width); // 800 * (200/400)
        Assert.Equal(0, pages[0].Rect.X);
        Assert.Equal(400, pages[1].Rect.X); // after page 0's 400px width
        Assert.Equal(200, pages[1].Rect.Width); // 400 * (200/400)
    }

    [Fact]
    public void ComputeContinuousLayout_OnlyVisiblePagesPlusRadius_AreReturned()
    {
        // 10 same-size pages, each exactly the viewport's height (100), viewport shows only page 3.
        var sizes = Enumerable.Range(0, 10).Select(_ => new Size(400, 100)).ToArray();

        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 300, new Size(400, 100), ReaderLayoutModel.Axis.Vertical, virtualizationRadius: 2);

        // Page 3 (offset 300-399) is the only one intersecting the viewport (300-399) - window is
        // [3-2, 3+2] = [1, 5].
        Assert.Equal(Enumerable.Range(1, 5), pages.Select(p => p.Index));
    }

    [Fact]
    public void ComputeContinuousLayout_VirtualizationWindow_ClampsAtStartAndEnd()
    {
        var sizes = Enumerable.Range(0, 4).Select(_ => new Size(400, 100)).ToArray();

        // Viewport shows page 0 (first page) - radius 2 would want [-2, 2], clamped to [0, 2].
        var atStart = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(400, 100), ReaderLayoutModel.Axis.Vertical, virtualizationRadius: 2);
        Assert.Equal(new[] { 0, 1, 2 }, atStart.Select(p => p.Index));

        // Viewport shows page 3 (last page) - radius 2 would want [1, 5], clamped to [1, 3].
        var atEnd = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 300, new Size(400, 100), ReaderLayoutModel.Axis.Vertical, virtualizationRadius: 2);
        Assert.Equal(new[] { 1, 2, 3 }, atEnd.Select(p => p.Index));
    }

    [Fact]
    public void ComputeContinuousLayout_Zoom_ScalesCrossAxisSize()
    {
        var sizes = new[] { new Size(400, 600) };

        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(400, 600), ReaderLayoutModel.Axis.Vertical, zoom: 2.0);

        var page = Assert.Single(pages);
        Assert.Equal(800, page.Rect.Width); // 400 viewport width * 2x zoom
        Assert.Equal(1200, page.Rect.Height); // scaled proportionally with the wider cross axis
    }

    [Fact]
    public void ComputeContinuousLayout_ZoomedPage_IsCenteredByDefault()
    {
        var sizes = new[] { new Size(400, 600) };

        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(400, 600), ReaderLayoutModel.Axis.Vertical, zoom: 2.0);

        // Viewport width 400, page now 800 wide - centered means X = (400-800)/2 = -200.
        Assert.Equal(-200, pages[0].Rect.X);
    }

    [Fact]
    public void ComputeContinuousLayout_CrossAxisPanOffset_ShiftsZoomedPageSideways()
    {
        var sizes = new[] { new Size(400, 600) };

        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(400, 600), ReaderLayoutModel.Axis.Vertical, zoom: 2.0, crossAxisPanOffset: 50);

        Assert.Equal(-150, pages[0].Rect.X); // -200 centered + 50 pan
    }

    [Fact]
    public void ComputeContinuousLayout_DefaultZoomAndPan_MatchesUnzoomedBehavior()
    {
        var sizes = new[] { new Size(400, 600) };

        var withDefaults = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(400, 600), ReaderLayoutModel.Axis.Vertical);
        var explicitUnzoomed = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(400, 600), ReaderLayoutModel.Axis.Vertical, zoom: 1.0, crossAxisPanOffset: 0.0);

        Assert.Equal(explicitUnzoomed[0].Rect, withDefaults[0].Rect);
    }

    [Fact]
    public void ComputeContinuousLayout_EmptyPageList_ReturnsEmpty()
    {
        var pages = ReaderLayoutModel.ComputeContinuousLayout(Array.Empty<Size>(), 0, new Size(400, 600), ReaderLayoutModel.Axis.Vertical);
        Assert.Empty(pages);
    }

    [Fact]
    public void ComputeContinuousLayout_ScrollPastEveryPage_ReturnsEmpty()
    {
        var sizes = new[] { new Size(400, 100) };
        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 1000, new Size(400, 100), ReaderLayoutModel.Axis.Vertical);
        Assert.Empty(pages);
    }

    [Fact]
    public void ComputeContinuousLayout_MainAxisGap_InsertsSpaceBetweenPages()
    {
        var sizes = new[] { new Size(400, 600), new Size(400, 300) };

        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(400, 100), ReaderLayoutModel.Axis.Vertical, virtualizationRadius: 5, mainAxisGap: 20);

        Assert.Equal(0, pages[0].Rect.Y);
        Assert.Equal(620, pages[1].Rect.Y); // 600 (page 0 height) + 20 gap
    }

    [Fact]
    public void ComputeContinuousLayout_ZeroGap_MatchesMergedWebtoonBehavior()
    {
        var sizes = new[] { new Size(400, 600), new Size(400, 300) };

        var pages = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(400, 100), ReaderLayoutModel.Axis.Vertical, virtualizationRadius: 5, mainAxisGap: 0);

        Assert.Equal(600, pages[1].Rect.Y); // no gap - page 1 starts exactly where page 0 ends
    }

    [Fact]
    public void ComputeContinuousLayout_ReverseMainAxis_StacksPageZeroAtTrailingEdge()
    {
        var sizes = new[] { new Size(200, 400), new Size(200, 400) };

        var forward = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(100, 400), ReaderLayoutModel.Axis.Horizontal, virtualizationRadius: 5);
        var reversed = ReaderLayoutModel.ComputeContinuousLayout(sizes, scrollOffset: 0, new Size(100, 400), ReaderLayoutModel.Axis.Horizontal, virtualizationRadius: 5, reverseMainAxis: true);

        Assert.Equal(0, forward[0].Rect.X); // LTR: page 0 starts at the origin, grows rightward (+X)

        // RTL: page 0's *trailing* edge is flush with the viewport's own trailing edge (100), not
        // mirrored around X=0 - the real bug found via manual testing (mirroring around 0 put page
        // 0 entirely off-screen, a blank reader for every scroll position). Left edge at
        // 100 - 200 = -100, right edge at -100 + 200 = 100 (== viewport width).
        Assert.Equal(-100, reversed[0].Rect.X);
        Assert.Equal(100, reversed[0].Rect.X + reversed[0].Rect.Width);
    }

    [Fact]
    public void ComputeTotalMainAxisSize_IncludesGapsBetweenPages_ButNotAfterLastPage()
    {
        var sizes = new[] { new Size(400, 600), new Size(400, 300), new Size(400, 100) };

        double total = ReaderLayoutModel.ComputeTotalMainAxisSize(sizes, new Size(400, 100), ReaderLayoutModel.Axis.Vertical, mainAxisGap: 20);

        Assert.Equal(1040, total); // 600+300+100 + 2 gaps (between 3 pages, not trailing)
    }

    [Fact]
    public void ComputeTotalMainAxisSize_SumsEveryPagesScaledMainAxisSize()
    {
        var sizes = new[] { new Size(400, 600), new Size(400, 300) };

        double total = ReaderLayoutModel.ComputeTotalMainAxisSize(sizes, new Size(400, 100), ReaderLayoutModel.Axis.Vertical);

        Assert.Equal(900, total); // both already at viewport width (400), so heights sum unscaled
    }

    [Fact]
    public void ComputeTotalMainAxisSize_ScalesWithZoom()
    {
        var sizes = new[] { new Size(400, 600) };

        double total = ReaderLayoutModel.ComputeTotalMainAxisSize(sizes, new Size(400, 100), ReaderLayoutModel.Axis.Vertical, zoom: 2.0);

        Assert.Equal(1200, total); // cross axis doubles, main axis scales proportionally
    }

    [Fact]
    public void NearestPageToViewportCenter_Vertical_PicksPageWhoseMidpointIsClosestToCenter()
    {
        // Viewport is 400 tall, center at y=200. Page 0 spans 0-150 (midpoint 75), page 1 spans
        // 150-500 (midpoint 325) - page 1's midpoint (325) is closer to 200 than page 0's (75)?
        // |325-200|=125 vs |75-200|=125 - tie, so use a clearer case below instead.
        var pages = new[]
        {
            new ReaderLayoutModel.LayoutPage(0, new Rect(0, 0, 400, 150)),
            new ReaderLayoutModel.LayoutPage(1, new Rect(0, 150, 400, 100)), // midpoint 200 - exact center
            new ReaderLayoutModel.LayoutPage(2, new Rect(0, 250, 400, 400)),
        };

        int nearest = ReaderLayoutModel.NearestPageToViewportCenter(pages, new Size(400, 400), ReaderLayoutModel.Axis.Vertical);

        Assert.Equal(1, nearest);
    }

    [Fact]
    public void NearestPageToViewportCenter_Horizontal_UsesXMidpoint()
    {
        var pages = new[]
        {
            new ReaderLayoutModel.LayoutPage(0, new Rect(0, 0, 50, 300)),
            new ReaderLayoutModel.LayoutPage(1, new Rect(50, 0, 200, 300)), // midpoint x=150 - exact center of a 300-wide viewport
        };

        int nearest = ReaderLayoutModel.NearestPageToViewportCenter(pages, new Size(300, 300), ReaderLayoutModel.Axis.Horizontal);

        Assert.Equal(1, nearest);
    }

    [Fact]
    public void NearestPageToViewportCenter_EmptyList_ReturnsNegativeOne()
    {
        int nearest = ReaderLayoutModel.NearestPageToViewportCenter(Array.Empty<ReaderLayoutModel.LayoutPage>(), new Size(400, 400), ReaderLayoutModel.Axis.Vertical);
        Assert.Equal(-1, nearest);
    }
}
