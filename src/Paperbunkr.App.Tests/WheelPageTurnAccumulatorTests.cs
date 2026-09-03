using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="WheelPageTurnAccumulator"/> - the plain-wheel page-turn debounce behind
/// <see cref="PageCanvas"/>'s paged-mode wheel handling (user report 2026-09-03: a precision
/// touchpad's stream of sub-detent deltas flipped through many pages per swipe). Pure state
/// machine, no Avalonia input plumbing - same split-out pattern as <c>PageTurnGestureMathTests</c>.
/// </summary>
public class WheelPageTurnAccumulatorTests
{
    // --- Real mouse wheel: one detent = one turn, immediately, unchanged ---

    [Theory]
    [InlineData(-1.0, 1)]   // wheel down -> forward
    [InlineData(1.0, -1)]   // wheel up -> back
    [InlineData(-3.0, 1)]   // a coarse-notch wheel still just turns once per event
    public void FullDetent_TurnsImmediately(double deltaY, int expected)
    {
        var acc = new WheelPageTurnAccumulator();
        Assert.Equal(expected, acc.Accumulate(WheelPageTurnAccumulator.ForwardScalar(0, deltaY)));
    }

    [Fact]
    public void RepeatedFullDetents_TurnEveryTime()
    {
        var acc = new WheelPageTurnAccumulator();
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(1, acc.Accumulate(WheelPageTurnAccumulator.ForwardScalar(0, -1.0)));
        }
    }

    // --- Precision touchpad: sub-detent deltas accumulate to one turn per detent's worth ---

    [Fact]
    public void SubDetentDeltas_DoNotTurnUntilAFullDetentAccumulates()
    {
        var acc = new WheelPageTurnAccumulator();

        Assert.Equal(0, acc.Accumulate(0.3));
        Assert.Equal(0, acc.Accumulate(0.3));
        Assert.Equal(0, acc.Accumulate(0.3)); // 0.9 total - still short
        Assert.Equal(1, acc.Accumulate(0.3)); // crosses 1.0 -> one forward turn
        Assert.Equal(0, acc.Accumulate(0.3)); // remainder was dropped, building again
    }

    [Fact]
    public void OneLongSwipe_TurnsRoughlyProportionalToDistance_NotPerEvent()
    {
        var acc = new WheelPageTurnAccumulator();
        int turns = 0;
        // 20 touchpad events of 0.25 each = 5.0 total -> ~5 pages, not 20.
        for (int i = 0; i < 20; i++)
        {
            if (acc.Accumulate(0.25) != 0)
            {
                turns++;
            }
        }

        Assert.Equal(5, turns);
    }

    // --- Direction reversal drops opposite-sign buildup ---

    [Fact]
    public void ReversingDirection_ClearsPartialBuildup()
    {
        var acc = new WheelPageTurnAccumulator();

        acc.Accumulate(0.6); // 0.6 toward forward
        Assert.Equal(0, acc.Accumulate(-0.6)); // reversal: buildup cleared, now 0.6 toward back
        Assert.Equal(-1, acc.Accumulate(-0.6)); // 1.2 toward back -> back turn
    }

    // --- Reset ---

    [Fact]
    public void Reset_DropsPartialBuildup()
    {
        var acc = new WheelPageTurnAccumulator();

        acc.Accumulate(0.9);
        acc.Reset();
        Assert.Equal(0, acc.Accumulate(0.9)); // would have crossed 1.0 without the reset
    }

    // --- ForwardScalar convention: wheel-down or wheel-right = forward ---

    [Theory]
    [InlineData(0, -1.0, true)]   // wheel down -> forward (positive scalar)
    [InlineData(0, 1.0, false)]   // wheel up -> back
    [InlineData(1.0, 0, true)]    // wheel/tilt right -> forward
    [InlineData(-1.0, 0, false)]  // wheel/tilt left -> back
    public void ForwardScalar_MatchesPageCanvasConvention(double deltaX, double deltaY, bool forward)
    {
        double scalar = WheelPageTurnAccumulator.ForwardScalar(deltaX, deltaY);
        Assert.Equal(forward, scalar > 0);
    }

    [Fact]
    public void ZeroDelta_IsNoOp()
    {
        var acc = new WheelPageTurnAccumulator();
        Assert.Equal(0, acc.Accumulate(0));
    }
}
