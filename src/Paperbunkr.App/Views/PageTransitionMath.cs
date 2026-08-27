using System;

namespace Paperbunkr.App.Views;

/// <summary>
/// Pure page-turn transition geometry/opacity, shared by nothing else (unlike
/// <see cref="ZoomPanMath"/>) but kept as its own static class for the same reason: a small, plain-
/// input helper <see cref="ReaderPageVisualHandler"/>'s render path can call without needing a real
/// compositor/render context, so the interpolation itself is testable in isolation
/// (docs/superpowers/specs/2026-08-13-reader-page-transition-animations-design.md §5). Internal, not
/// <c>public</c> like <see cref="ZoomPanMath"/> - <see cref="SlideOffset"/> takes
/// <see cref="PageTransitionDirection"/>, itself internal, so this can't be more visible than that
/// without also exposing the enum.
/// </summary>
internal static class PageTransitionMath
{
    /// <summary>
    /// Slide offsets along the turn's main axis, for a turn in <paramref name="direction"/> at
    /// animation <paramref name="progress"/> (0 = turn just started, 1 = turn complete). The main
    /// axis is the viewport's width for <see cref="PageTransitionDirection.Left"/>/
    /// <see cref="PageTransitionDirection.Right"/> and its height for
    /// <see cref="PageTransitionDirection.Up"/>/<see cref="PageTransitionDirection.Down"/> - the
    /// caller passes the right <paramref name="mainAxisExtent"/> and applies the returned offsets
    /// to the matching axis. A "forward" turn (<see cref="PageTransitionDirection.Right"/> or
    /// <see cref="PageTransitionDirection.Down"/>) brings the incoming page in from the trailing
    /// edge while the outgoing page exits toward the leading edge; a backward turn
    /// (<see cref="PageTransitionDirection.Left"/>/<see cref="PageTransitionDirection.Up"/>) mirrors
    /// it. This is spatial, not reading-direction, naming (docs/superpowers/specs/2026-08-07-reader-
    /// rtl-navigation-design.md §3; vertical directions per 2026-08-27-vertical-paged-reading-mode-
    /// design.md).
    /// </summary>
    public static (double OutgoingOffset, double IncomingOffset) SlideOffset(PageTransitionDirection direction, double progress, double mainAxisExtent)
    {
        double p = Math.Clamp(progress, 0, 1);
        double sign = direction is PageTransitionDirection.Right or PageTransitionDirection.Down ? 1 : -1;

        double outgoingOffset = -sign * p * mainAxisExtent;
        double incomingOffset = sign * (1 - p) * mainAxisExtent;
        return (outgoingOffset, incomingOffset);
    }

    /// <summary>Crossfade alpha at <paramref name="progress"/> (0 = fully outgoing, 1 = fully incoming) - a plain linear blend, matching CE's own <c>FadeInBlending</c> (a straight <c>percent</c> alpha ramp, confirmed from source).</summary>
    public static (double OutgoingAlpha, double IncomingAlpha) CrossfadeAlpha(double progress)
    {
        double p = Math.Clamp(progress, 0, 1);
        return (1 - p, p);
    }
}
