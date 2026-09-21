using System;
using Avalonia;
using Avalonia.Media;

namespace Paperbunkr.App.Views;

/// <summary>
/// Shared circular-arc geometry, extracted from <c>Stats/CategoryDonut</c> so the tile read-progress
/// ring (<see cref="TileCosmeticsOverlay"/>, docs/superpowers/specs/2026-09-21-cosmetics-pitch-design.md
/// #2) draws arcs the same way the Insights donut does. Angles are radians, 0 = 3 o'clock, clockwise.
/// </summary>
public static class ArcGeometry
{
    /// <summary>12 o'clock, where both the donut and the progress ring start sweeping.</summary>
    public const double TopAngle = -Math.PI / 2;

    public static Point PointOnCircle(Point center, double radius, double angle)
        => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));

    /// <summary>An open clockwise arc from <paramref name="startAngle"/> sweeping <paramref name="sweep"/> radians.</summary>
    public static StreamGeometry CreateArc(Point center, double radius, double startAngle, double sweep)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(PointOnCircle(center, radius, startAngle), false);
            ctx.ArcTo(
                PointOnCircle(center, radius, startAngle + sweep), new Size(radius, radius),
                0, sweep > Math.PI, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        return geometry;
    }
}
