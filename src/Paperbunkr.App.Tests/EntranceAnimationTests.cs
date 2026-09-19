using System.Diagnostics;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The one-shot entrance window (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md §2):
/// armed by a genuine trigger, opened by the first container preparation after it, and closed 500 ms
/// later - so containers a panel recycles while scrolling are never animated.
/// </summary>
public class EntranceAnimationTests
{
    private static long Ms(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000.0);

    [Fact]
    public void NeverArmed_IsClosed()
    {
        var window = new EntranceAnimation.EntranceWindow();

        Assert.False(window.IsOpen(Ms(0)));
        Assert.False(window.IsOpen(Ms(10_000)));
    }

    [Fact]
    public void Armed_OpensAtTheFirstQuery_AndStaysOpenForTheDuration()
    {
        var window = new EntranceAnimation.EntranceWindow();
        window.Arm();

        Assert.True(window.IsOpen(Ms(1_000)));    // first preparation opens it
        Assert.True(window.IsOpen(Ms(1_499)));    // still inside 500 ms
        Assert.False(window.IsOpen(Ms(1_501)));   // a container recycled by scrolling afterwards: closed
        Assert.False(window.IsOpen(Ms(60_000)));
    }

    [Fact]
    public void ASlowFirstLayout_CannotExpireTheWindow()
    {
        var window = new EntranceAnimation.EntranceWindow();
        window.Arm();

        // The window is measured from the first preparation, not from the trigger.
        Assert.True(window.IsOpen(Ms(30_000)));
        Assert.True(window.IsOpen(Ms(30_400)));
    }

    [Fact]
    public void ArmingAgain_ReopensAtTheNextQuery()
    {
        var window = new EntranceAnimation.EntranceWindow();
        window.Arm();
        Assert.True(window.IsOpen(Ms(0)));
        Assert.False(window.IsOpen(Ms(2_000)));

        window.Arm();

        Assert.True(window.IsOpen(Ms(5_000)));
        Assert.True(window.IsOpen(Ms(5_300)));
        Assert.False(window.IsOpen(Ms(5_600)));
    }

    [Fact]
    public void Duration_IsHalfASecond()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), EntranceAnimation.EntranceWindow.Duration);
    }
}
