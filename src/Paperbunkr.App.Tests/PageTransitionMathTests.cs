using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PageTransitionMath"/>'s pure slide/crossfade interpolation
/// (docs/superpowers/specs/2026-08-13-reader-page-transition-animations-design.md §5). Plain
/// numeric inputs - no Avalonia app context or compositor needed.
/// </summary>
public class PageTransitionMathTests
{
    [Fact]
    public void SlideOffset_Right_AtProgressZero_OutgoingAtStart_IncomingOffscreenRight()
    {
        var (outgoing, incoming) = PageTransitionMath.SlideOffset(PageTransitionDirection.Right, 0, 400);

        Assert.Equal(0, outgoing);
        Assert.Equal(400, incoming);
    }

    [Fact]
    public void SlideOffset_Right_AtProgressOne_OutgoingOffscreenLeft_IncomingAtRest()
    {
        var (outgoing, incoming) = PageTransitionMath.SlideOffset(PageTransitionDirection.Right, 1, 400);

        Assert.Equal(-400, outgoing);
        Assert.Equal(0, incoming);
    }

    [Fact]
    public void SlideOffset_Right_AtHalfProgress_BothHalfway()
    {
        var (outgoing, incoming) = PageTransitionMath.SlideOffset(PageTransitionDirection.Right, 0.5, 400);

        Assert.Equal(-200, outgoing);
        Assert.Equal(200, incoming);
    }

    [Fact]
    public void SlideOffset_Left_MirrorsRight()
    {
        var right = PageTransitionMath.SlideOffset(PageTransitionDirection.Right, 0.5, 400);
        var left = PageTransitionMath.SlideOffset(PageTransitionDirection.Left, 0.5, 400);

        Assert.Equal(-right.OutgoingOffset, left.OutgoingOffset);
        Assert.Equal(-right.IncomingOffset, left.IncomingOffset);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void SlideOffset_Down_MatchesRight_And_Up_MatchesLeft(double progress)
    {
        // Vertical paged mode (docs/superpowers/specs/2026-08-27-vertical-paged-reading-mode-design.md):
        // Down is the "forward" turn like Right, Up is "backward" like Left. The caller applies the
        // returned offsets to Y instead of X.
        Assert.Equal(
            PageTransitionMath.SlideOffset(PageTransitionDirection.Right, progress, 700),
            PageTransitionMath.SlideOffset(PageTransitionDirection.Down, progress, 700));
        Assert.Equal(
            PageTransitionMath.SlideOffset(PageTransitionDirection.Left, progress, 700),
            PageTransitionMath.SlideOffset(PageTransitionDirection.Up, progress, 700));
    }

    [Fact]
    public void SlideOffset_ClampsProgressBelowZero()
    {
        var atNegative = PageTransitionMath.SlideOffset(PageTransitionDirection.Right, -5, 400);
        var atZero = PageTransitionMath.SlideOffset(PageTransitionDirection.Right, 0, 400);

        Assert.Equal(atZero, atNegative);
    }

    [Fact]
    public void SlideOffset_ClampsProgressAboveOne()
    {
        var atTooHigh = PageTransitionMath.SlideOffset(PageTransitionDirection.Right, 5, 400);
        var atOne = PageTransitionMath.SlideOffset(PageTransitionDirection.Right, 1, 400);

        Assert.Equal(atOne, atTooHigh);
    }

    [Fact]
    public void CrossfadeAlpha_AtProgressZero_FullyOutgoing()
    {
        var (outgoingAlpha, incomingAlpha) = PageTransitionMath.CrossfadeAlpha(0);

        Assert.Equal(1, outgoingAlpha);
        Assert.Equal(0, incomingAlpha);
    }

    [Fact]
    public void CrossfadeAlpha_AtProgressOne_FullyIncoming()
    {
        var (outgoingAlpha, incomingAlpha) = PageTransitionMath.CrossfadeAlpha(1);

        Assert.Equal(0, outgoingAlpha);
        Assert.Equal(1, incomingAlpha);
    }

    [Fact]
    public void CrossfadeAlpha_AtHalfProgress_EvenBlend()
    {
        var (outgoingAlpha, incomingAlpha) = PageTransitionMath.CrossfadeAlpha(0.5);

        Assert.Equal(0.5, outgoingAlpha);
        Assert.Equal(0.5, incomingAlpha);
    }

    [Fact]
    public void CrossfadeAlpha_ClampsOutsideZeroToOneRange()
    {
        Assert.Equal(PageTransitionMath.CrossfadeAlpha(0), PageTransitionMath.CrossfadeAlpha(-2));
        Assert.Equal(PageTransitionMath.CrossfadeAlpha(1), PageTransitionMath.CrossfadeAlpha(2));
    }
}
