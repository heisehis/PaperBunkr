using System;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Pure offset rule for <see cref="ParallaxBackdrop"/> (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md C9). The image
/// is drawn with <paramref name="overscanFraction"/> of the frame height spare above and below, and moves the opposite way to the
/// scroll by <paramref name="factor"/>, clamped so it can never run out of spare image - the reason the 2026-09-07 version (no
/// overscan) was removed.
/// </summary>
public static class ParallaxMath
{
    public const double DefaultFactor = 0.35;
    public const double DefaultOverscan = 0.125;

    /// <param name="scrolledPast">How far the frame's top has scrolled above the viewport top, in DIPs (0 or negative = not yet).</param>
    public static double Offset(double scrolledPast, double frameHeight, double factor = DefaultFactor, double overscanFraction = DefaultOverscan)
    {
        if (frameHeight <= 0 || scrolledPast <= 0)
        {
            return 0;
        }

        double limit = frameHeight * overscanFraction;
        return Math.Clamp(scrolledPast * factor, 0, limit);
    }
}
