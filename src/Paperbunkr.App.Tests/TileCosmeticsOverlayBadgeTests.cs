using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>Placement of the membership dots on a cover (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md).</summary>
public class TileCosmeticsOverlayBadgeTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 3)]
    public void AtMostThreeDotsAreDrawn(int count, int expected)
    {
        Assert.Equal(expected, TileCosmeticsOverlay.BadgeDotRects(count, 200).Count);
    }

    [Fact]
    public void Dots_SitBottomLeft_AboveTheBar_AndNeverOverlap()
    {
        const double height = 200;
        var rects = TileCosmeticsOverlay.BadgeDotRects(3, height);

        Assert.All(rects, r =>
        {
            Assert.True(r.Bottom <= height - TileCosmeticsOverlay.BarHeight, "clear of the progress bar");
            Assert.True(r.Left >= 0 && r.Right < 60, "in the bottom-left corner, far from the bottom-right one");
        });
        for (int i = 1; i < rects.Count; i++)
        {
            Assert.True(rects[i].Left >= rects[i - 1].Right + TileCosmeticsOverlay.BadgeGap - 0.001, "spaced");
        }
    }

    [Fact]
    public void Dots_FollowTheCoverHeight()
    {
        var small = TileCosmeticsOverlay.BadgeDotRects(1, 120)[0];
        var big = TileCosmeticsOverlay.BadgeDotRects(1, 300)[0];
        Assert.Equal(180, big.Top - small.Top, 3);
    }
}
