using System;
using Avalonia;
using Paperbunkr.App.Views.Stats;

namespace Paperbunkr.App.Tests;

/// <summary>Exercises <see cref="DonutMath"/>, the angle arithmetic the donut draws and hit-tests with
/// (docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-design.md).</summary>
public class DonutMathTests
{
    [Fact]
    public void Sweeps_CloseTheRingExactly()
    {
        var sweeps = DonutMath.Sweeps(new[] { 2177, 798, 480, 135, 1 });
        Assert.Equal(Math.PI * 2, sweeps.Sum(), 6);
    }

    [Fact]
    public void Sweeps_GiveATinySliceItsMinimum_WithoutBreakingTheLargest()
    {
        var sweeps = DonutMath.Sweeps(new[] { 5000, 1 });
        Assert.True(sweeps[1] >= DonutMath.MinSweep);
        Assert.True(sweeps[0] > 0);
    }

    [Fact]
    public void Sweeps_OfNothing_AreEmptyOrZero()
    {
        Assert.Empty(DonutMath.Sweeps(Array.Empty<int>()));
        Assert.All(DonutMath.Sweeps(new[] { 0, 0 }), s => Assert.Equal(0, s));
    }

    // A 100x100 donut: centre (50,50), outer radius 50, ring 14 wide.
    private static readonly Point Centre = new(50, 50);

    [Fact]
    public void HitTest_JustRightOfTwelveOClock_IsTheFirstSlice_AndJustLeftIsTheLast()
    {
        var sweeps = DonutMath.Sweeps(new[] { 1, 1, 1 });

        Assert.Equal(0, DonutMath.HitTest(sweeps, Centre, 50, 14, new Point(53, 5)));
        Assert.Equal(2, DonutMath.HitTest(sweeps, Centre, 50, 14, new Point(47, 5)));
    }

    [Fact]
    public void HitTest_FollowsTheSweepClockwise()
    {
        var sweeps = DonutMath.Sweeps(new[] { 1, 1, 1, 1 }); // quarters
        // 3 o'clock is the end of quarter 0 / start of quarter 1; sample inside each quarter.
        Assert.Equal(0, DonutMath.HitTest(sweeps, Centre, 50, 14, new Point(90, 20)));
        Assert.Equal(1, DonutMath.HitTest(sweeps, Centre, 50, 14, new Point(90, 80)));
        Assert.Equal(2, DonutMath.HitTest(sweeps, Centre, 50, 14, new Point(10, 80)));
        Assert.Equal(3, DonutMath.HitTest(sweeps, Centre, 50, 14, new Point(10, 20)));
    }

    [Fact]
    public void HitTest_OffTheRing_IsNone()
    {
        var sweeps = DonutMath.Sweeps(new[] { 1, 1 });

        Assert.Equal(-1, DonutMath.HitTest(sweeps, Centre, 50, 14, Centre)); // the hole
        Assert.Equal(-1, DonutMath.HitTest(sweeps, Centre, 50, 14, new Point(0, 0))); // outside the circle
    }
}
