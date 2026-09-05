using System;
using Avalonia;

namespace Paperbunkr.App.Services;

/// <summary>
/// Pure geometry for the cover-art "shared element" flight (docs/superpowers/specs/2026-09-04-
/// navigation-transition-system-design.md) - no visual-tree dependency, so
/// <see cref="ISharedElementTransitionService"/>'s clone placement is a thin wrapper around this
/// rather than doing the arithmetic itself. Assumes the clone's <c>RenderTransformOrigin</c> is
/// top-left (0,0) - <see cref="ComputeFlight"/> returns a plain translate+scale, not a
/// origin-relative one, so callers must set that origin to get the intended motion.
/// </summary>
public static class SharedElementFlightMath
{
    /// <summary>
    /// Computes the translate/scale needed to move a clone initially placed at <paramref name="source"/>
    /// (top-left origin) so it exactly covers <paramref name="destination"/>, plus the corner-radius
    /// endpoints to interpolate between. <paramref name="source"/>/<paramref name="destination"/> are
    /// both expected in the same coordinate space (the overlay host's), e.g. via
    /// <c>Visual.TransformToVisual(overlayHost)</c>.
    /// </summary>
    public static SharedElementFlight ComputeFlight(Rect source, Rect destination, CornerRadius sourceRadius, CornerRadius destinationRadius)
    {
        if (!IsUsable(source) || !IsUsable(destination))
        {
            return new SharedElementFlight(0, 0, 1, 1, sourceRadius, destinationRadius, IsNoOp: true);
        }

        double translateX = destination.X - source.X;
        double translateY = destination.Y - source.Y;
        double scaleX = destination.Width / source.Width;
        double scaleY = destination.Height / source.Height;

        return new SharedElementFlight(translateX, translateY, scaleX, scaleY, sourceRadius, destinationRadius, IsNoOp: false);
    }

    /// <summary>A rect with non-finite or non-positive width/height can't anchor a scale transform - treated as "no flight" by <see cref="ComputeFlight"/> rather than dividing by zero/producing NaN/Infinity.</summary>
    private static bool IsUsable(Rect r) =>
        r.Width > 0 && r.Height > 0 &&
        double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.Width) && double.IsFinite(r.Height);
}

/// <param name="IsNoOp">True when either rect was zero-size or non-finite - callers should skip animating and fall back to a plain cross-fade rather than trusting <see cref="TranslateX"/>/<see cref="ScaleX"/> etc.</param>
public readonly record struct SharedElementFlight(
    double TranslateX,
    double TranslateY,
    double ScaleX,
    double ScaleY,
    CornerRadius StartRadius,
    CornerRadius EndRadius,
    bool IsNoOp);
