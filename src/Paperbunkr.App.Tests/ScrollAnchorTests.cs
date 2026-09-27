using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>Scroll anchoring for continuous mode (docs/superpowers/specs/2026-09-25-comic-reader-performance-design.md B4).</summary>
public class ScrollAnchorTests
{
    // Five pages of 1000 px each with a 0 px gap: page i spans [1000 i, 1000 i + 1000).
    private static readonly double[] Even = [1000, 1000, 1000, 1000, 1000];

    [Theory]
    [InlineData(0, 0)]
    [InlineData(999, 0)]
    [InlineData(1000, 1)] // exactly at a page boundary the next page is the first visible one
    [InlineData(2500, 2)]
    [InlineData(4999, 4)]
    public void FirstVisibleIndex_IsTheFirstPageEndingBeyondTheOffset(double offset, int expected)
    {
        Assert.Equal(expected, ScrollAnchor.FirstVisibleIndex(Even, gap: 0, offset));
    }

    [Fact]
    public void FirstVisibleIndex_PastEveryPage_IsMinusOne()
    {
        Assert.Equal(-1, ScrollAnchor.FirstVisibleIndex(Even, gap: 0, 5000));
    }

    [Fact]
    public void FirstVisibleIndex_CountsTheGapBetweenPages()
    {
        // pages span [0,100) [150,250) [300,400): an offset of 120 sits in the gap, so the next page is the first visible one
        Assert.Equal(1, ScrollAnchor.FirstVisibleIndex([100, 100, 100], gap: 50, 120));
    }

    [Fact]
    public void OffsetDelta_PageAboveTheViewportGrows_ShiftsByTheGrowth()
    {
        // viewport top is inside page 2; page 1 turns out 300 px taller than estimated
        Assert.Equal(300, ScrollAnchor.OffsetDelta(Even, 0, scrollOffset: 2400, changedIndex: 1, newMainSize: 1300));
    }

    [Fact]
    public void OffsetDelta_PageAboveTheViewportShrinks_ShiftsBackwards()
    {
        Assert.Equal(-250, ScrollAnchor.OffsetDelta(Even, 0, 2400, 0, 750));
    }

    [Fact]
    public void OffsetDelta_TheFirstVisiblePageItself_NeedsNoCorrection()
    {
        Assert.Equal(0, ScrollAnchor.OffsetDelta(Even, 0, 2400, 2, 1500));
    }

    [Fact]
    public void OffsetDelta_PagesBelowTheViewportTop_NeedNoCorrection()
    {
        Assert.Equal(0, ScrollAnchor.OffsetDelta(Even, 0, 2400, 3, 1500));
        Assert.Equal(0, ScrollAnchor.OffsetDelta(Even, 0, 2400, 4, 200));
    }

    [Fact]
    public void OffsetDelta_AtTheVeryTop_HasNothingAbove()
    {
        Assert.Equal(0, ScrollAnchor.OffsetDelta(Even, 0, 0, 0, 1800));
    }

    [Fact]
    public void OffsetDelta_NoChange_IsZero()
    {
        Assert.Equal(0, ScrollAnchor.OffsetDelta(Even, 0, 2400, 1, 1000));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void OffsetDelta_OutOfRangeIndex_IsZero(int index)
    {
        Assert.Equal(0, ScrollAnchor.OffsetDelta(Even, 0, 2400, index, 1500));
    }

    [Fact]
    public void OffsetDelta_OffsetPastEverything_IsZero()
    {
        Assert.Equal(0, ScrollAnchor.OffsetDelta(Even, 0, 9999, 0, 1500));
    }

    [Fact]
    public void Anchoring_KeepsTheFirstVisiblePageTopOnScreen()
    {
        // The property the whole feature exists for: after the shift, the same page top is still at the same screen position.
        double gap = 10;
        double[] before = [800, 900, 1000, 1100];
        double offset = 2000; // page 2 spans [1720+... compute below
        int firstVisible = ScrollAnchor.FirstVisibleIndex(before, gap, offset);
        double topBefore = StackTop(before, gap, firstVisible) - offset;

        double[] after = [800, 1250, 1000, 1100]; // page 1 grew by 350
        double delta = ScrollAnchor.OffsetDelta(before, gap, offset, 1, 1250);
        double topAfter = StackTop(after, gap, firstVisible) - (offset + delta);

        Assert.Equal(topBefore, topAfter, precision: 6);
    }

    private static double StackTop(double[] sizes, double gap, int index)
    {
        double top = 0;
        for (int i = 0; i < index; i++)
        {
            top += sizes[i] + gap;
        }

        return top;
    }
}
