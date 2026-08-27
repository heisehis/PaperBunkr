using System;
using Avalonia;

namespace Paperbunkr.App.Views;

/// <summary>
/// Pure page-turn intent resolution for <see cref="PageCanvas"/>'s click/tap zones and touch
/// flicks (docs/superpowers/specs/2026-08-27-vertical-paged-reading-mode-design.md §3). One helper
/// covers both the horizontal paged modes (<see cref="Data.Entities.ReadingMode.LeftToRight"/>/
/// <see cref="Data.Entities.ReadingMode.RightToLeft"/>) and the vertical one
/// (<see cref="Data.Entities.ReadingMode.TopToBottom"/>), so the four call sites - mouse zone,
/// touch zone, single-finger flick, two-finger-drag flick - don't each carry a parallel axis
/// switch. Testable without Avalonia input plumbing, same split-out-the-pure-math pattern as
/// <see cref="ZoomPanMath"/>.
///
/// Returns are "forward?" - <c>true</c> = advance a page, <c>false</c> = go back, <c>null</c> = no
/// turn (dead zone, or a flick too short / too diagonal).
/// </summary>
internal static class PageTurnGestureMath
{
    /// <summary>
    /// Which way a tap/click at <paramref name="point"/> turns, in a <paramref name="bounds"/>-sized
    /// canvas. <paramref name="divisions"/> is 2 (mouse: near half = back, far half = forward) or 3
    /// (touch: near third = back, far third = forward, middle third = <c>null</c>). Splits on Y when
    /// <paramref name="vertical"/>, on X otherwise.
    /// </summary>
    public static bool? ResolveZone(Point point, Size bounds, bool vertical, int divisions)
    {
        double pos = vertical ? point.Y : point.X;
        double extent = vertical ? bounds.Height : bounds.Width;
        if (extent <= 0)
        {
            return null;
        }

        if (divisions == 2)
        {
            return pos >= extent / 2;
        }

        double third = extent / 3;
        if (pos < third)
        {
            return false;
        }

        if (pos > third * 2)
        {
            return true;
        }

        return null;
    }

    /// <summary>
    /// Which way a flick of <paramref name="delta"/> (end − start) turns, or <c>null</c> if it's
    /// shorter than <paramref name="minDistance"/> along its dominant axis or too diagonal
    /// (dominant-axis travel must strictly exceed the cross-axis). Horizontal: flick left
    /// (<c>delta.X &lt; 0</c>) = forward, the spatial convention. Vertical: flick up
    /// (<c>delta.Y &lt; 0</c>) = forward - flick the content up to advance, matching the wheel and
    /// the continuous scroll modes.
    /// </summary>
    public static bool? ResolveFlick(Vector delta, bool vertical, double minDistance)
    {
        double primary = vertical ? delta.Y : delta.X;
        double secondary = vertical ? delta.X : delta.Y;

        if (Math.Abs(primary) < minDistance || Math.Abs(primary) <= Math.Abs(secondary))
        {
            return null;
        }

        return primary < 0;
    }
}
