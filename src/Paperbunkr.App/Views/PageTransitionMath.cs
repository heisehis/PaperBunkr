using System;
using System.Collections.Generic;
using Avalonia;

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

    /// <summary>Ease-in-out for the physical styles (Book, Curl): a page accelerates off the stack and settles, it does not move linearly.</summary>
    public static double Smooth(double progress)
    {
        double p = Math.Clamp(progress, 0, 1);
        return p * p * (3 - (2 * p));
    }

    /// <summary>
    /// Book (CE <c>Paging</c>): only one page moves. Reading forward, the <i>new</i> page sweeps in over the still old one from the edge the turn
    /// comes from; reading backward, the <i>old</i> page slides off the other way, uncovering the previous page that sits still beneath it.
    /// <paramref name="direction"/> is spatial (as in <see cref="SlideOffset"/>); <paramref name="forwardReading"/> says which of the two
    /// that is, so right-to-left reading gets the mirrored book. Returns which side moves and its offset along the main axis.
    /// </summary>
    public static (bool NewMoves, double Offset) BookOffset(PageTransitionDirection direction, bool forwardReading, double progress, double mainAxisExtent)
    {
        double e = Smooth(progress);
        double sign = direction is PageTransitionDirection.Right or PageTransitionDirection.Down ? 1 : -1;
        return forwardReading ? (true, sign * (1 - e) * mainAxisExtent) : (false, -sign * e * mainAxisExtent);
    }

    /// <summary>Whether a spatial turn reads forward: rightward/downward in left-to-right reading, leftward in right-to-left.</summary>
    public static bool IsForwardReading(PageTransitionDirection direction, bool isRightToLeft) => direction switch
    {
        PageTransitionDirection.Right => !isRightToLeft,
        PageTransitionDirection.Left => isRightToLeft,
        PageTransitionDirection.Down => true,
        _ => false,
    };

    /// <summary>
    /// Curl geometry for <paramref name="page"/> at <paramref name="progress"/>. The outer bottom corner (bottom-right, or bottom-left when
    /// <paramref name="mirrored"/> for right-to-left reading) is dragged across and slightly up the page and off the far edge; the fold is the
    /// perpendicular bisector of corner→drag point. Returns a point on the fold and the fold's unit normal, which points toward the corner -
    /// the turned-over side.
    /// </summary>
    public static (Point FoldPoint, Vector Normal, bool Active) CurlFold(Rect page, double progress, bool mirrored)
    {
        double e = Smooth(progress);
        double sign = mirrored ? 1 : -1;
        var corner = new Point(mirrored ? page.Left : page.Right, page.Bottom);
        // Corner first: the lift (y) leads and the sweep (x, eased in) follows, so the page peels up before it travels across.
        var drag = new Point(corner.X + (sign * 2.05 * page.Width * Math.Pow(e, 1.5)), corner.Y - (0.36 * page.Height * Math.Sin(Math.PI * e)));
        var delta = corner - drag;
        double length = Math.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y));
        if (length < 0.5)
        {
            return (corner, new Vector(-sign, 0), false);
        }

        return (new Point((corner.X + drag.X) / 2, (corner.Y + drag.Y) / 2), new Vector(delta.X / length, delta.Y / length), true);
    }

    /// <summary>The rectangle's corners in order.</summary>
    public static List<Point> Corners(Rect r) => [r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft];

    /// <summary>Clips a convex polygon to one side of a line (Sutherland-Hodgman): <paramref name="normalSide"/> keeps the side the normal
    /// points to, otherwise the other side.</summary>
    public static List<Point> ClipToSide(IReadOnlyList<Point> polygon, Point on, Vector normal, bool normalSide)
    {
        double Side(Point p) => ((p.X - on.X) * normal.X) + ((p.Y - on.Y) * normal.Y);
        bool Inside(Point p) => normalSide ? Side(p) >= 0 : Side(p) <= 0;

        var result = new List<Point>();
        for (int i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            bool currentIn = Inside(current);
            bool nextIn = Inside(next);
            if (currentIn)
            {
                result.Add(current);
            }

            if (currentIn != nextIn)
            {
                double a = Side(current);
                double b = Side(next);
                double t = a / (a - b);
                result.Add(new Point(current.X + ((next.X - current.X) * t), current.Y + ((next.Y - current.Y) * t)));
            }
        }

        return result;
    }

    /// <summary>Mirrors <paramref name="point"/> across the line through <paramref name="on"/> with unit normal <paramref name="normal"/> -
    /// where the turned-over part of the page lands when it folds flat.</summary>
    public static Point Reflect(Point point, Point on, Vector normal)
    {
        double d = ((point.X - on.X) * normal.X) + ((point.Y - on.Y) * normal.Y);
        return new Point(point.X - (2 * d * normal.X), point.Y - (2 * d * normal.Y));
    }
}
