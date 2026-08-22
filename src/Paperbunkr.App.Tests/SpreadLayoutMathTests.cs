using Avalonia;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="SpreadLayoutMath"/>'s pure pairing/sizing math (docs/superpowers/specs/
/// 2026-08-15-reader-double-page-spread-design.md §2/§4). Plain <see cref="PixelSize"/>/
/// <see cref="Rect"/> inputs - no Avalonia app context needed.
/// </summary>
public class SpreadLayoutMathTests
{
    [Fact]
    public void IsPairEligible_BothPortrait_ReturnsTrue() =>
        Assert.True(SpreadLayoutMath.IsPairEligible(new PixelSize(600, 900), new PixelSize(500, 1000)));

    [Fact]
    public void IsPairEligible_BothSquare_ReturnsTrue() =>
        Assert.True(SpreadLayoutMath.IsPairEligible(new PixelSize(600, 600), new PixelSize(500, 500)));

    [Fact]
    public void IsPairEligible_FirstLandscape_ReturnsFalse() =>
        Assert.False(SpreadLayoutMath.IsPairEligible(new PixelSize(1200, 800), new PixelSize(600, 900)));

    [Fact]
    public void IsPairEligible_SecondLandscape_ReturnsFalse() =>
        Assert.False(SpreadLayoutMath.IsPairEligible(new PixelSize(600, 900), new PixelSize(1200, 800)));

    [Fact]
    public void IsPairEligible_BothLandscape_ReturnsFalse() =>
        Assert.False(SpreadLayoutMath.IsPairEligible(new PixelSize(1200, 800), new PixelSize(1100, 700)));

    [Fact]
    public void ComputeCombinedSize_SameAspectDifferentSize_SplitsEvenly()
    {
        // Both 0.5 aspect (width:height) - scaling b up to a's height should land it at exactly a's
        // own width, so the two halves split the combined width evenly.
        var result = SpreadLayoutMath.ComputeCombinedSize(new PixelSize(400, 800), new PixelSize(300, 600));

        Assert.Equal(800, result.Combined.Width);
        Assert.Equal(800, result.Combined.Height);
        Assert.Equal(0.5, result.LeftWidthFraction, precision: 6);
    }

    [Fact]
    public void ComputeCombinedSize_DifferentAspects_WeightsWiderPageMoreHeavily()
    {
        // a: 600x900 (aspect 0.667), b: 800x1000 (aspect 0.8) - b is proportionally wider once
        // scaled to a's common height, so it should take up more than half the combined width.
        var result = SpreadLayoutMath.ComputeCombinedSize(new PixelSize(600, 900), new PixelSize(800, 1000));

        Assert.True(result.LeftWidthFraction < 0.5);
    }

    [Fact]
    public void SplitSpread_SubRectsSumToSourceWidth()
    {
        var destRect = new Rect(10, 20, 800, 400);

        var (left, right) = SpreadLayoutMath.SplitSpread(destRect, leftWidthFraction: 0.6);

        Assert.Equal(destRect.X, left.X);
        Assert.Equal(destRect.Width, left.Width + right.Width, precision: 6);
        Assert.Equal(left.X + left.Width, right.X, precision: 6);
        Assert.Equal(destRect.Height, left.Height);
        Assert.Equal(destRect.Height, right.Height);
    }

    [Fact]
    public void SplitSpread_AtHalf_ProducesEqualSubRects()
    {
        var destRect = new Rect(0, 0, 800, 400);

        var (left, right) = SpreadLayoutMath.SplitSpread(destRect, leftWidthFraction: 0.5);

        Assert.Equal(400, left.Width);
        Assert.Equal(400, right.Width);
    }
}
