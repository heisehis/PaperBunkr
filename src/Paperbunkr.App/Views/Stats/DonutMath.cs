using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace Paperbunkr.App.Views.Stats;

/// <summary>
/// The angle arithmetic <see cref="CategoryDonut"/> draws with, pulled out so the hover hit-test and the render loop
/// cannot disagree about where a slice is. Angles are radians, clockwise from <see cref="ArcGeometry.TopAngle"/>.
/// </summary>
public static class DonutMath
{
    /// <summary>Every non-empty slice gets at least this sweep, so a 1-of-2,000 slice is still visible.</summary>
    public const double MinSweep = Math.PI / 60;

    /// <summary>The full-ring sweep of each slice: <see cref="MinSweep"/> plus its share of what is left of the circle once every slice's
    /// minimum is set aside, so the sweeps add up to exactly one turn. (The first version of this also subtracted the padding from the
    /// largest slice, which counted it twice and left the ring a few degrees short - round line caps used to hide that.)</summary>
    public static double[] Sweeps(IReadOnlyList<int> counts)
    {
        var sweeps = new double[counts.Count];
        double total = counts.Sum(c => (double)c);
        if (counts.Count == 0 || total <= 0)
        {
            return sweeps;
        }

        double scale = Math.Max(0, (Math.PI * 2) - (counts.Count * MinSweep)) / total;
        for (int i = 0; i < counts.Count; i++)
        {
            sweeps[i] = (counts[i] * scale) + MinSweep;
        }

        return sweeps;
    }

    /// <summary>The index of the slice under <paramref name="point"/>, or -1 when the point is off the ring. <paramref name="outerRadius"/>
    /// is the donut's outside edge; the ring is <paramref name="thickness"/> wide inside it.</summary>
    public static int HitTest(IReadOnlyList<double> sweeps, Point center, double outerRadius, double thickness, Point point)
    {
        double dx = point.X - center.X;
        double dy = point.Y - center.Y;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance > outerRadius + 2 || distance < outerRadius - thickness - 2)
        {
            return -1;
        }

        double angle = Math.Atan2(dy, dx) - ArcGeometry.TopAngle;
        angle %= Math.PI * 2;
        if (angle < 0)
        {
            angle += Math.PI * 2;
        }

        double cursor = 0;
        for (int i = 0; i < sweeps.Count; i++)
        {
            cursor += sweeps[i];
            if (angle < cursor)
            {
                return i;
            }
        }

        return -1;
    }
}
