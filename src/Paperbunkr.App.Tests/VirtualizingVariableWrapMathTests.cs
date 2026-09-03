using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="VirtualizingVariableWrapMath"/> - the pure greedy row-packing behind
/// <see cref="VirtualizingVariableWrapPanel"/>, the variable-width virtualizing panel Panorama uses
/// so each cover renders at its real aspect ratio. Same "test the geometry directly" split as
/// <see cref="VirtualizingWrapGridMathTests"/>.
/// </summary>
public class VirtualizingVariableWrapMathTests
{
    // --- ComputeLayout ---

    [Fact]
    public void ComputeLayout_PacksItemsLeftToRight_WrappingWhenTheNextItemWouldOverflow()
    {
        // 330px available. Widths 100 each, 10 spacing -> 0, 110, 220 fit (a 4th at 330 overflows),
        // so row 0 = items 0-2, row 1 = item 3.
        double[] widths = { 100, 100, 100, 100 };
        var layout = VirtualizingVariableWrapMath.ComputeLayout(widths, availableWidth: 330, itemHeight: 150, itemSpacing: 10, lineSpacing: 12);

        Assert.Equal(new[] { 0, 0, 0, 1 }, layout.RowOfItem);
        Assert.Equal(new[] { 0.0, 110.0, 220.0, 0.0 }, layout.XOfItem);
        Assert.Equal(2, layout.TotalRows);
        Assert.Equal(2 * (150 + 12) - 12, layout.TotalHeight);
    }

    [Fact]
    public void ComputeLayout_WideAndNarrowTilesShareARow_UntilTheRowIsFull()
    {
        // A landscape cover (220) then two portraits (90, 90): 220 + 10 + 90 = 320 <= 500,
        // + 10 + 90 = 420 <= 500 -> all three on row 0. Fourth (90) at 430+... 520 > 500 -> row 1.
        double[] widths = { 220, 90, 90, 90 };
        var layout = VirtualizingVariableWrapMath.ComputeLayout(widths, availableWidth: 500, itemHeight: 146, itemSpacing: 10, lineSpacing: 10);

        Assert.Equal(new[] { 0, 0, 0, 1 }, layout.RowOfItem);
        Assert.Equal(3, layout.FirstItemOfRow[1]); // row 1 starts at item 3
    }

    [Fact]
    public void ComputeLayout_AlwaysPlacesAtLeastOneItemPerRow_EvenIfItAloneOverflows()
    {
        double[] widths = { 600, 600, 600 };
        var layout = VirtualizingVariableWrapMath.ComputeLayout(widths, availableWidth: 300, itemHeight: 150, itemSpacing: 10, lineSpacing: 10);

        Assert.Equal(new[] { 0, 1, 2 }, layout.RowOfItem);
        Assert.Equal(3, layout.TotalRows);
    }

    [Fact]
    public void ComputeLayout_FirstItemOfRow_HasOneEntryPerRowPlusACountSentinel()
    {
        double[] widths = { 100, 100, 100, 100, 100 }; // 330 wide -> rows [0,1,2],[3,4]
        var layout = VirtualizingVariableWrapMath.ComputeLayout(widths, availableWidth: 330, itemHeight: 150, itemSpacing: 10, lineSpacing: 10);

        Assert.Equal(2, layout.TotalRows);
        Assert.Equal(new[] { 0, 3, 5 }, layout.FirstItemOfRow); // last entry == item count
    }

    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    public void ComputeLayout_ReturnsEmpty_ForDegenerateInputs(double itemHeight)
    {
        var empty = VirtualizingVariableWrapMath.ComputeLayout(
            itemHeight == 0 ? new[] { 100.0 } : System.Array.Empty<double>(),
            availableWidth: 500, itemHeight: itemHeight, itemSpacing: 10, lineSpacing: 10);

        Assert.Equal(0, empty.TotalRows);
        Assert.Equal(0, empty.TotalHeight);
    }

    [Fact]
    public void ComputeLayout_ReturnsEmpty_ForInfiniteAvailableWidth()
    {
        var layout = VirtualizingVariableWrapMath.ComputeLayout(
            new[] { 100.0, 120.0 }, availableWidth: double.PositiveInfinity, itemHeight: 150, itemSpacing: 10, lineSpacing: 10);
        Assert.Equal(0, layout.TotalRows);
    }

    // --- ComputeRealizedRange ---

    [Fact]
    public void ComputeRealizedRange_PicksItemsWhoseRowsIntersectTheViewport_PlusBuffer()
    {
        // 12 items, 3 per row (100px each, 330 wide) -> 4 rows, stride 160 (150 + 10).
        double[] widths = System.Linq.Enumerable.Repeat(100.0, 12).ToArray();
        var layout = VirtualizingVariableWrapMath.ComputeLayout(widths, availableWidth: 330, itemHeight: 150, itemSpacing: 10, lineSpacing: 10);
        Assert.Equal(4, layout.TotalRows);

        // Viewport y=320..480 -> floor(320/160)=2, floor(480/160)=3; buffer 1 -> rows 1..4 (clamped
        // to 3) -> items [firstOfRow[1], count-1] = [3, 11].
        var range = VirtualizingVariableWrapMath.ComputeRealizedRange(layout, viewportTop: 320, viewportBottom: 480, itemHeight: 150, lineSpacing: 10, bufferRows: 1);
        Assert.Equal(3, range.FirstIndex);
        Assert.Equal(11, range.LastIndex);
    }

    [Fact]
    public void ComputeRealizedRange_ClampsToRealItems_AtBothEnds()
    {
        double[] widths = System.Linq.Enumerable.Repeat(100.0, 10).ToArray(); // 2/row -> rows [0,1]..[8,9]
        var layout = VirtualizingVariableWrapMath.ComputeLayout(widths, availableWidth: 300, itemHeight: 100, itemSpacing: 10, lineSpacing: 10);

        var whole = VirtualizingVariableWrapMath.ComputeRealizedRange(layout, viewportTop: 0, viewportBottom: 100000, itemHeight: 100, lineSpacing: 10, bufferRows: 2);
        Assert.Equal(0, whole.FirstIndex);
        Assert.Equal(9, whole.LastIndex); // not 11 - last row holds only item 9
    }

    [Fact]
    public void ComputeRealizedRange_IsEmpty_ForAnEmptyLayout()
    {
        var range = VirtualizingVariableWrapMath.ComputeRealizedRange(VariableWrapLayout.Empty, 0, 500, 150, 10, 2);
        Assert.True(range.IsEmpty);
    }

    // --- NearestInRow (Up/Down arrow-key target) ---

    [Fact]
    public void NearestInRow_ReturnsTheItemClosestInXToTheAnchor()
    {
        double[] widths = { 100, 100, 100, 80, 200, 90 }; // 500 wide: row0 = 0,1,2,3 (x 0,110,220,330); row1 = 4,5
        var layout = VirtualizingVariableWrapMath.ComputeLayout(widths, availableWidth: 500, itemHeight: 150, itemSpacing: 10, lineSpacing: 10);
        Assert.Equal(2, layout.TotalRows);

        // Anchor at x=225 (item 2's column). Row 1: item 4 at x=0, item 5 at x=210 -> item 5 wins.
        Assert.Equal(5, VirtualizingVariableWrapMath.NearestInRow(layout, targetRow: 1, anchorX: 225));
    }

    [Fact]
    public void NearestInRow_ReturnsMinusOne_ForARowOutOfRange()
    {
        var layout = VirtualizingVariableWrapMath.ComputeLayout(new[] { 100.0, 100.0 }, availableWidth: 300, itemHeight: 150, itemSpacing: 10, lineSpacing: 10);
        Assert.Equal(-1, VirtualizingVariableWrapMath.NearestInRow(layout, targetRow: -1, anchorX: 0));
        Assert.Equal(-1, VirtualizingVariableWrapMath.NearestInRow(layout, targetRow: 5, anchorX: 0));
    }
}
