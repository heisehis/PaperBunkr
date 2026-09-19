using System;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Pure math for eased mouse-wheel scrolling (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md §6),
/// separated from <see cref="SmoothScrollViewer"/> for direct testing.
/// </summary>
public static class WheelEasingMath
{
    /// <summary>
    /// Pixels one mouse-wheel notch scrolls: Avalonia's own stock distance (design decision D4 - no new step size to relearn).
    /// Measured, not assumed: <c>Paperbunkr.ScrollHarness --wheel-probe</c> raised wheel deltas of 1, 2 and 3 on a stock
    /// <c>ScrollViewer</c> and it moved 50, 100 and 150 px.
    /// </summary>
    public const double StockNotchPixels = 50;

    /// <summary>Exponential-smoothing time constant. The offset covers ~63 % of the remaining distance per 60 ms and is visually settled in ~180 ms.</summary>
    public const double TimeConstantSeconds = 0.06;

    /// <summary>Closer than this to the target (in pixels) counts as arrived.</summary>
    public const double SnapDistancePixels = 0.5;

    /// <summary>
    /// True for a discrete mouse-wheel notch: a whole-number vertical delta and no horizontal component. Touchpads and free-spinning
    /// wheels report small fractional deltas and already scroll continuously, so they pass through untouched.
    /// </summary>
    public static bool IsMouseWheelNotch(double deltaX, double deltaY) =>
        deltaX == 0 && Math.Abs(deltaY) >= 1 && Math.Abs(deltaY - Math.Round(deltaY)) < 0.01;

    /// <summary>New scroll target after <paramref name="notches"/> wheel notches (positive = wheel up = toward the top), clamped to the extent.</summary>
    public static double Retarget(double basis, double notches, double maxOffset) =>
        Math.Clamp(basis - (notches * StockNotchPixels), 0, Math.Max(0, maxOffset));

    /// <summary>One animation step toward <paramref name="target"/> over <paramref name="dtSeconds"/>; returns <paramref name="target"/> exactly once within <see cref="SnapDistancePixels"/>.</summary>
    public static double Advance(double current, double target, double dtSeconds)
    {
        if (Math.Abs(target - current) <= SnapDistancePixels)
        {
            return target;
        }

        double k = 1 - Math.Exp(-Math.Max(0, dtSeconds) / TimeConstantSeconds);
        double next = current + ((target - current) * k);
        return Math.Abs(target - next) <= SnapDistancePixels ? target : next;
    }

    /// <summary>True when something other than the animation moved the offset (scrollbar drag, keyboard, code): the animation must stand down.</summary>
    public static bool MovedExternally(double lastSetByAnimation, double actualOffset) =>
        Math.Abs(actualOffset - lastSetByAnimation) > 1.0;
}
