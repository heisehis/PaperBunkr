using Avalonia;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PagePartMath"/>'s pure split-page part-navigation grid math (docs/
/// superpowers/specs/2026-09-05-reader-polish-backlog-finish-design.md §1). Plain <see cref="Size"/>/
/// <see cref="PixelSize"/> inputs - no Avalonia app context needed.
/// </summary>
public class PagePartMathTests
{
    [Fact]
    public void ComputePartGrid_ContentFitsViewport_ReturnsOneByOne()
    {
        var grid = PagePartMath.ComputePartGrid(new Size(800, 1200), new PixelSize(600, 900), zoom: 1.0);

        Assert.Equal((1, 1), grid);
    }

    [Fact]
    public void ComputePartGrid_OversizedBothAxes_ReturnsExpectedGrid()
    {
        // Viewport 400x600; content 800x1800 at zoom 1, ImageFitMode.Original (no auto-fit
        // shrinking) - displayed size is exactly the content's own pixel size, 2x the viewport
        // width and 3x its height.
        var grid = PagePartMath.ComputePartGrid(new Size(400, 600), new PixelSize(800, 1800), zoom: 1.0, ImageFitMode.Original);

        Assert.Equal((2, 3), grid);
    }

    [Fact]
    public void ComputePartGrid_OversizedWidthOnly_ReturnsSingleRow()
    {
        var grid = PagePartMath.ComputePartGrid(new Size(400, 600), new PixelSize(900, 500), zoom: 1.0, ImageFitMode.Original);

        Assert.Equal((3, 1), grid);
    }

    [Fact]
    public void PartCount_MultipliesGridDimensions() => Assert.Equal(6, PagePartMath.PartCount((2, 3)));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void FindNearestPart_AtEachCellCenter_ReturnsThatCell(int expectedIndex, int _)
    {
        var viewport = new Size(400, 400);
        var content = new PixelSize(800, 800);
        double scale = 1.0; // Original fit mode at zoom 1, matching content's own pixel size.
        var grid = PagePartMath.ComputePartGrid(viewport, content, zoom: 1.0, ImageFitMode.Original);
        Assert.Equal((2, 2), grid);

        var (panX, panY) = PagePartMath.PanForPart(expectedIndex, grid, viewport, content, scale, zoom: 1.0, rightToLeft: false, ImageFitMode.Original);

        int found = PagePartMath.FindNearestPart(grid, viewport, content, scale, panX, panY, rightToLeft: false);

        Assert.Equal(expectedIndex, found);
    }

    [Fact]
    public void FindNearestPart_RightToLeft_ReversesColumnOrderWithinRow()
    {
        var viewport = new Size(400, 400);
        var content = new PixelSize(800, 400);
        double scale = 1.0;
        var grid = PagePartMath.ComputePartGrid(viewport, content, zoom: 1.0, ImageFitMode.Original);
        Assert.Equal((2, 1), grid);

        // RTL part 0 is the rightmost column; panning to LTR part 0 (leftmost) should resolve as
        // RTL part 1 instead.
        var (panX, panY) = PagePartMath.PanForPart(0, grid, viewport, content, scale, zoom: 1.0, rightToLeft: false, ImageFitMode.Original);

        int foundRtl = PagePartMath.FindNearestPart(grid, viewport, content, scale, panX, panY, rightToLeft: true);

        Assert.Equal(1, foundRtl);
    }

    [Fact]
    public void PanForPart_RoundTripsThroughFindNearestPart_ForEveryCellIn2x2Grid()
    {
        var viewport = new Size(400, 400);
        var content = new PixelSize(800, 800);
        double scale = 1.0;
        var grid = PagePartMath.ComputePartGrid(viewport, content, zoom: 1.0, ImageFitMode.Original);

        for (int i = 0; i < PagePartMath.PartCount(grid); i++)
        {
            var (panX, panY) = PagePartMath.PanForPart(i, grid, viewport, content, scale, zoom: 1.0, rightToLeft: false, ImageFitMode.Original);
            int found = PagePartMath.FindNearestPart(grid, viewport, content, scale, panX, panY, rightToLeft: false);
            Assert.Equal(i, found);
        }
    }

    [Fact]
    public void PanForPart_RoundTripsThroughFindNearestPart_ForEveryCellIn3x1Grid()
    {
        var viewport = new Size(400, 300);
        var content = new PixelSize(1200, 300);
        double scale = 1.0;
        var grid = PagePartMath.ComputePartGrid(viewport, content, zoom: 1.0, ImageFitMode.Original);
        Assert.Equal((3, 1), grid);

        for (int i = 0; i < PagePartMath.PartCount(grid); i++)
        {
            var (panX, panY) = PagePartMath.PanForPart(i, grid, viewport, content, scale, zoom: 1.0, rightToLeft: false, ImageFitMode.Original);
            int found = PagePartMath.FindNearestPart(grid, viewport, content, scale, panX, panY, rightToLeft: false);
            Assert.Equal(i, found);
        }
    }

    [Fact]
    public void ComputePartGrid_DoublePageSpreadCombinedSize_AccountsForFullCombinedWidth()
    {
        // Two 400x800 portrait pages side by side (SpreadLayoutMath's own combined-size formula,
        // same common-height convention) - combined width is double one page's width, so at zoom 1
        // Original fit against a single-page-width viewport this should read as a 2-wide grid
        // rather than the 1-wide grid a solo page's own PixelSize would produce.
        var combined = SpreadLayoutMath.ComputeCombinedSize(new PixelSize(400, 800), new PixelSize(400, 800));
        var viewport = new Size(400, 800);

        var soloGrid = PagePartMath.ComputePartGrid(viewport, new PixelSize(400, 800), zoom: 1.0, ImageFitMode.Original);
        var spreadGrid = PagePartMath.ComputePartGrid(viewport, combined.Combined, zoom: 1.0, ImageFitMode.Original);

        Assert.Equal((1, 1), soloGrid);
        Assert.Equal((2, 1), spreadGrid);
    }
}
