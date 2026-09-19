using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Eased mouse-wheel scrolling (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md section 6): the pure math and the
/// per-viewer state machine. The Avalonia wiring (<see cref="SmoothScrollViewer"/>) is checked on screen.
/// </summary>
public class WheelEasingTests
{
    // ---------------------------------------------------------------- notch classification

    [Theory]
    [InlineData(0, 1, true)]     // one notch up
    [InlineData(0, -1, true)]    // one notch down
    [InlineData(0, -3, true)]    // three notches in one event
    [InlineData(0, 0.1, false)]  // touchpad / precise
    [InlineData(0, -0.25, false)]
    [InlineData(0, 1.5, false)]  // a free-spinning wheel between notches
    [InlineData(1, 1, false)]    // horizontal component: not ours
    [InlineData(0, 0, false)]
    public void IsMouseWheelNotch_OnlyForWholeVerticalNotches(double dx, double dy, bool expected)
    {
        Assert.Equal(expected, WheelEasingMath.IsMouseWheelNotch(dx, dy));
    }

    [Fact]
    public void TheNotchDistance_IsTheStockAvaloniaOneOf50Pixels()
    {
        // Pinned from the harness's --wheel-probe: a stock ScrollViewer moved 50, 100, 150 px for wheel deltas of 1, 2, 3.
        Assert.Equal(50, WheelEasingMath.StockNotchPixels);
    }

    // ---------------------------------------------------------------- targets

    [Fact]
    public void Retarget_MovesByTheStockDistancePerNotch_DownIsPositive()
    {
        Assert.Equal(150, WheelEasingMath.Retarget(basis: 100, notches: -1, maxOffset: 10_000));
        Assert.Equal(200, WheelEasingMath.Retarget(basis: 100, notches: -2, maxOffset: 10_000));
        Assert.Equal(50, WheelEasingMath.Retarget(basis: 100, notches: 1, maxOffset: 10_000));
    }

    [Fact]
    public void Retarget_IsClampedToTheExtent()
    {
        Assert.Equal(0, WheelEasingMath.Retarget(basis: 20, notches: 5, maxOffset: 1_000));
        Assert.Equal(1_000, WheelEasingMath.Retarget(basis: 990, notches: -5, maxOffset: 1_000));
        Assert.Equal(0, WheelEasingMath.Retarget(basis: 0, notches: -1, maxOffset: 0));
    }

    // ---------------------------------------------------------------- stepping

    [Fact]
    public void Advance_MovesTowardTheTarget_WithoutOvershooting_AndSnapsWhenClose()
    {
        double offset = 0;
        double previous = -1;
        for (int frame = 0; frame < 200; frame++)
        {
            offset = WheelEasingMath.Advance(offset, 100, dtSeconds: 1.0 / 60);
            Assert.InRange(offset, previous, 100);
            previous = offset;
        }

        Assert.Equal(100, offset); // exactly, not 99.9999
    }

    [Fact]
    public void Advance_IsEasedOut_TheFirstStepsAreTheBiggest()
    {
        double first = WheelEasingMath.Advance(0, 100, 1.0 / 60);
        double second = WheelEasingMath.Advance(first, 100, 1.0 / 60) - first;

        Assert.True(first > second, $"first step {first} should exceed second {second}");
    }

    [Fact]
    public void Advance_SettlesInAboutThreeTenthsOfASecond()
    {
        double offset = 0;
        int frames = 0;
        while (offset != 50 && frames < 1000)
        {
            offset = WheelEasingMath.Advance(offset, 50, 1.0 / 60);
            frames++;
        }

        double seconds = frames / 60.0;
        Assert.InRange(seconds, 0.15, 0.5);
    }

    [Fact]
    public void Advance_ZeroOrNegativeTime_DoesNotMove()
    {
        Assert.Equal(0, WheelEasingMath.Advance(0, 100, 0));
        Assert.Equal(0, WheelEasingMath.Advance(0, 100, -1));
    }

    [Fact]
    public void MovedExternally_IsTrueOnlyBeyondOnePixel()
    {
        Assert.False(WheelEasingMath.MovedExternally(100, 100.5));
        Assert.True(WheelEasingMath.MovedExternally(100, 102));
    }

    // ---------------------------------------------------------------- controller

    [Fact]
    public void Controller_AnAdditionalNotchDuringTheAnimation_AccumulatesFromTheTarget_NotTheCurrentOffset()
    {
        var controller = new WheelScrollController();

        controller.AddNotches(-1, currentOffset: 0, maxOffset: 10_000);
        controller.Tick(0, 1.0 / 60, 10_000);
        controller.AddNotches(-1, currentOffset: 10, maxOffset: 10_000);

        Assert.Equal(100, controller.Target); // 0 -> 50 target, then +50 more, not 10 + 50
    }

    [Fact]
    public void Controller_RunsToTheTarget_ThenIsIdle()
    {
        var controller = new WheelScrollController();
        controller.AddNotches(-2, 0, 10_000);
        double offset = 0;

        for (int frame = 0; frame < 300 && controller.IsAnimating; frame++)
        {
            offset = controller.Tick(offset, 1.0 / 60, 10_000) ?? offset;
        }

        Assert.Equal(100, offset);
        Assert.False(controller.IsAnimating);
        Assert.Null(controller.Tick(offset, 1.0 / 60, 10_000));
    }

    [Fact]
    public void Controller_StandsDown_WhenSomethingElseMovesTheOffset()
    {
        var controller = new WheelScrollController();
        controller.AddNotches(-3, 0, 10_000);
        double offset = controller.Tick(0, 1.0 / 60, 10_000)!.Value;

        // The user grabs the scrollbar and jumps: the offset is no longer what the animation last set.
        double dragged = offset + 400;

        Assert.Null(controller.Tick(dragged, 1.0 / 60, 10_000));
        Assert.False(controller.IsAnimating);
    }

    [Fact]
    public void Controller_ClampsTheTargetIfTheExtentShrinksMidAnimation()
    {
        var controller = new WheelScrollController();
        controller.AddNotches(-10, 0, 10_000); // target 500
        double offset = controller.Tick(0, 1.0 / 60, 10_000)!.Value;

        double? next = controller.Tick(offset, 1.0 / 60, maxOffset: 60); // the list got shorter than where we are

        // The target is pulled inside the new extent and the offset eases back toward it (the ScrollViewer clamps the rest itself).
        Assert.NotNull(next);
        Assert.True(next < offset);
        Assert.True(controller.Target is null or <= 60);
    }

    [Fact]
    public void Controller_Cancel_StopsTheAnimation()
    {
        var controller = new WheelScrollController();
        controller.AddNotches(-1, 0, 10_000);

        controller.Cancel();

        Assert.False(controller.IsAnimating);
        Assert.Null(controller.Tick(0, 1.0 / 60, 10_000));
    }
}
