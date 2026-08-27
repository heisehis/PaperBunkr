using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="VirtualizingWrapGridMath"/> - the pure layout/realization arithmetic behind
/// <see cref="VirtualizingWrapPanel"/> (docs/superpowers/specs/2026-08-22-cover-memory-
/// virtualization-design.md), extracted for direct testing the same way <c>ZoomPanMath</c>/
/// <c>GridKeyboardNavigation</c> separate their pure geometry from the Avalonia-touching caller.
/// </summary>
public class VirtualizingWrapGridMathTests
{
    // --- ComputeLayout ---

    [Fact]
    public void ComputeLayout_FitsWholeItemsPerRow_IgnoringPartialLeftoverSpace()
    {
        // 500px available, 100px items, 10px spacing -> (100+10)*4=440 fits, a 5th would need 550.
        var layout = VirtualizingWrapGridMath.ComputeLayout(itemCount: 20, availableWidth: 500, itemWidth: 100, itemHeight: 150, itemSpacing: 10, lineSpacing: 10);

        Assert.Equal(4, layout.ItemsPerRow);
        Assert.Equal(5, layout.TotalRows); // ceil(20/4)
        Assert.Equal(5 * 150 + 4 * 10, layout.TotalHeight); // 5 rows, 4 gaps between them
    }

    [Fact]
    public void ComputeLayout_NeverGoesBelowOneItemPerRow_EvenWhenNarrowerThanOneItem()
    {
        var layout = VirtualizingWrapGridMath.ComputeLayout(itemCount: 5, availableWidth: 50, itemWidth: 100, itemHeight: 150, itemSpacing: 10, lineSpacing: 10);
        Assert.Equal(1, layout.ItemsPerRow);
        Assert.Equal(5, layout.TotalRows);
    }

    [Theory]
    [InlineData(0, 100, 100)] // no items
    [InlineData(5, 0, 100)]   // zero item width
    [InlineData(5, 100, 0)]   // zero item height
    public void ComputeLayout_ReturnsEmptyLayout_ForDegenerateInputs(int itemCount, double itemWidth, double itemHeight)
    {
        var layout = VirtualizingWrapGridMath.ComputeLayout(itemCount, availableWidth: 500, itemWidth, itemHeight, itemSpacing: 10, lineSpacing: 10);
        Assert.Equal(0, layout.TotalRows);
        Assert.Equal(0, layout.TotalHeight);
    }

    [Fact]
    public void ComputeLayout_ReturnsEmptyLayout_ForInfiniteAvailableWidth()
    {
        // A panel measured with Size.Infinity (e.g. inside a horizontally-scrolling ancestor) has
        // no meaningful "items per row" - must not throw or divide by infinity.
        var layout = VirtualizingWrapGridMath.ComputeLayout(itemCount: 5, availableWidth: double.PositiveInfinity, itemWidth: 100, itemHeight: 150, itemSpacing: 10, lineSpacing: 10);
        Assert.Equal(0, layout.TotalRows);
    }

    [Fact]
    public void ComputeLayout_ExactMultipleOfItemsPerRow_ProducesWholeRowCount()
    {
        var layout = VirtualizingWrapGridMath.ComputeLayout(itemCount: 12, availableWidth: 440, itemWidth: 100, itemHeight: 150, itemSpacing: 10, lineSpacing: 10);
        Assert.Equal(4, layout.ItemsPerRow);
        Assert.Equal(3, layout.TotalRows); // 12/4 exactly
    }

    // --- ComputeRealizedRange ---

    [Fact]
    public void ComputeRealizedRange_RealizesOnlyRowsNearTheViewport_PlusBuffer()
    {
        // 20 rows of 4 items = 80 items, row height 110 (100 item + 10 spacing). Viewport shows
        // rows covering y=500..700 (~rows 4-6), with a 2-row buffer each side -> rows 2-8.
        var layout = new WrapGridLayout(ItemsPerRow: 4, TotalRows: 20, TotalHeight: 20 * 110 - 10);

        var range = VirtualizingWrapGridMath.ComputeRealizedRange(
            itemCount: 80, layout, viewportTop: 500, viewportBottom: 700, itemHeight: 100, lineSpacing: 10, bufferRows: 2);

        // row 4 starts at 4*110=440 <= 500 < 550, row 6 covers 660-770 which includes 700.
        // firstRow = 4-2=2, lastRow = 6+2=8 -> indices [2*4, (8+1)*4-1] = [8, 35].
        Assert.Equal(8, range.FirstIndex);
        Assert.Equal(35, range.LastIndex);
    }

    [Fact]
    public void ComputeRealizedRange_ClampsAtTheStart_WhenViewportIsNearTheTop()
    {
        var layout = new WrapGridLayout(ItemsPerRow: 4, TotalRows: 20, TotalHeight: 20 * 110 - 10);

        var range = VirtualizingWrapGridMath.ComputeRealizedRange(
            itemCount: 80, layout, viewportTop: 0, viewportBottom: 200, itemHeight: 100, lineSpacing: 10, bufferRows: 2);

        Assert.Equal(0, range.FirstIndex); // never negative, even with a 2-row buffer above row 0
    }

    [Fact]
    public void ComputeRealizedRange_ClampsAtTheEnd_WhenViewportIsNearTheBottom()
    {
        var layout = new WrapGridLayout(ItemsPerRow: 4, TotalRows: 20, TotalHeight: 20 * 110 - 10);

        var range = VirtualizingWrapGridMath.ComputeRealizedRange(
            itemCount: 80, layout, viewportTop: 2000, viewportBottom: 2200, itemHeight: 100, lineSpacing: 10, bufferRows: 2);

        Assert.Equal(79, range.LastIndex); // never past the last real item, even with a trailing buffer
    }

    [Fact]
    public void ComputeRealizedRange_ClampsLastIndex_WhenTheLastRowIsPartiallyFilled()
    {
        // 10 items, 4 per row -> rows: [0-3],[4-7],[8-9] (last row only has 2 items).
        var layout = new WrapGridLayout(ItemsPerRow: 4, TotalRows: 3, TotalHeight: 3 * 110 - 10);

        var range = VirtualizingWrapGridMath.ComputeRealizedRange(
            itemCount: 10, layout, viewportTop: 0, viewportBottom: 10000, itemHeight: 100, lineSpacing: 10, bufferRows: 2);

        Assert.Equal(0, range.FirstIndex);
        Assert.Equal(9, range.LastIndex); // not 11 - the last row only has 2 real items
    }

    [Fact]
    public void ComputeRealizedRange_IsEmpty_WhenThereAreNoRows()
    {
        var layout = new WrapGridLayout(ItemsPerRow: 1, TotalRows: 0, TotalHeight: 0);
        var range = VirtualizingWrapGridMath.ComputeRealizedRange(
            itemCount: 0, layout, viewportTop: 0, viewportBottom: 500, itemHeight: 100, lineSpacing: 10, bufferRows: 2);

        Assert.True(range.IsEmpty);
    }

    // --- IndexToRowColumn ---

    [Theory]
    [InlineData(0, 4, 0, 0)]
    [InlineData(3, 4, 0, 3)]
    [InlineData(4, 4, 1, 0)]
    [InlineData(11, 4, 2, 3)]
    public void IndexToRowColumn_ComputesExpectedPosition(int index, int itemsPerRow, int expectedRow, int expectedColumn)
    {
        var (row, column) = VirtualizingWrapGridMath.IndexToRowColumn(index, itemsPerRow);
        Assert.Equal(expectedRow, row);
        Assert.Equal(expectedColumn, column);
    }
}
