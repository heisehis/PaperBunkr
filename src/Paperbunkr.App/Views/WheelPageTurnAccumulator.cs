using System;

namespace Paperbunkr.App.Views;

/// <summary>
/// Debounces plain-wheel page-turn intent for the paged (non-continuous) reading modes in
/// <see cref="PageCanvas"/>. A classic mouse wheel reports one full <c>±1.0</c> detent per physical
/// click, so one click naturally means one page turn. A Windows precision touchpad instead reports a
/// stream of small sub-detent deltas (typically <c>~0.05</c>-<c>0.4</c> each) for a single two-finger
/// swipe - and turning a page on every one of those events flips through a dozen pages from one
/// gesture (user report, 2026-09-03: "scroll sensitivity, especially on the touchpad").
///
/// The fix keys off the same distinction the OS already encodes: a delta whose magnitude is a full
/// detent (<c>&gt;= 1.0</c>) is passed straight through as an immediate turn - so spinning a real
/// wheel still pages exactly as fast as it is spun, unchanged - while sub-detent deltas are summed
/// and only produce a turn once a whole detent's worth has accumulated, with the remainder dropped.
/// A direction reversal clears any opposite-sign buildup so a swipe back doesn't have to unwind
/// first.
///
/// Pure and stateful-but-deterministic (no clock), unit-tested without Avalonia input plumbing -
/// same split-out-the-logic pattern as <see cref="PageTurnGestureMath"/> / <see cref="ZoomPanMath"/>.
/// </summary>
internal sealed class WheelPageTurnAccumulator
{
    /// <summary>One full mouse-wheel detent, as normalized by the platform backend (Win32:
    /// <c>WM_MOUSEWHEEL</c> delta / 120). Touchpad swipe events fall below this; discrete wheel
    /// clicks land exactly on multiples of it.</summary>
    private const double DetentThreshold = 1.0;

    private double _accumulated;

    /// <summary>Feeds one wheel event's forward-oriented scalar (positive = advance a page,
    /// negative = go back; see <see cref="ForwardScalar"/>).</summary>
    /// <returns><c>+1</c> to turn forward now, <c>-1</c> to turn back now, <c>0</c> for no turn yet.</returns>
    public int Accumulate(double forwardDelta)
    {
        if (forwardDelta == 0 || double.IsNaN(forwardDelta))
        {
            return 0;
        }

        // A full detent is already one deliberate click - turn immediately, and don't let it leave
        // a fractional remainder behind that could bias the next touchpad swipe.
        if (Math.Abs(forwardDelta) >= DetentThreshold)
        {
            _accumulated = 0;
            return forwardDelta > 0 ? 1 : -1;
        }

        if (_accumulated != 0 && Math.Sign(forwardDelta) != Math.Sign(_accumulated))
        {
            _accumulated = 0;
        }

        _accumulated += forwardDelta;

        if (_accumulated >= DetentThreshold)
        {
            _accumulated = 0;
            return 1;
        }

        if (_accumulated <= -DetentThreshold)
        {
            _accumulated = 0;
            return -1;
        }

        return 0;
    }

    /// <summary>Drops any partial buildup - call when the interaction context changes (a new book
    /// opened, a drag/click started, mode switched) so a stale half-swipe can't complete against an
    /// unrelated later one.</summary>
    public void Reset() => _accumulated = 0;

    /// <summary>Collapses a wheel event's two-axis <c>Delta</c> into a single forward-oriented
    /// scalar, matching <see cref="PageCanvas"/>'s existing convention: wheel-down
    /// (<c>Delta.Y &lt; 0</c>) or wheel-right (<c>Delta.X &gt; 0</c>) advances a page.</summary>
    public static double ForwardScalar(double deltaX, double deltaY) => deltaX - deltaY;
}
