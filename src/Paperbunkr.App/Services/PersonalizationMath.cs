using System;

namespace Paperbunkr.App.Services;

/// <summary>
/// The pure maths behind the Personalize controls (docs/superpowers/specs/2026-11-06-personalization-design.md): the Corners slider curve, the text scale and the
/// focus-ring stroke. No resources, no brushes, so it is unit-tested directly.
/// </summary>
public static class PersonalizationMath
{
    /// <summary>The theme defaults the slider's middle (50) reproduces.</summary>
    public const double DefaultRadius = 7, DefaultRadiusSm = 5, DefaultRadiusLg = 14;

    /// <summary>The rounded end (100): double the defaults.</summary>
    private const double MaxRadius = 14, MaxRadiusSm = 10, MaxRadiusLg = 28;

    public const int DefaultCorners = 50;

    public static readonly int[] FontScales = { 90, 100, 110 };

    public static int NormalizeFontScale(int percent) => Array.IndexOf(FontScales, percent) >= 0 ? percent : 100;

    public static int NormalizeCorners(int slider) => Math.Clamp(slider, 0, 100);

    /// <summary>Slider 0 = square, 50 = the theme's own radii, 100 = twice them; linear on each side of the middle.</summary>
    public static (double Radius, double RadiusSm, double RadiusLg) CornersToRadii(int slider)
    {
        slider = NormalizeCorners(slider);
        double t = slider <= 50 ? slider / 50.0 : (slider - 50) / 50.0;
        return slider <= 50
            ? (Round(DefaultRadius * t), Round(DefaultRadiusSm * t), Round(DefaultRadiusLg * t))
            : (Round(Lerp(DefaultRadius, MaxRadius, t)), Round(Lerp(DefaultRadiusSm, MaxRadiusSm, t)), Round(Lerp(DefaultRadiusLg, MaxRadiusLg, t)));
    }

    /// <summary>
    /// The slider value that produces these three radii (within half a unit each), or null when they are not on the curve - the slider then shows "Custom".
    /// </summary>
    public static int? RadiiToCorners(double radius, double radiusSm, double radiusLg)
    {
        // The closest slider value on the curve, not the first within tolerance (several neighbours are within half a unit).
        int? best = null;
        double bestError = double.MaxValue;
        for (int slider = 0; slider <= 100; slider++)
        {
            var (r, sm, lg) = CornersToRadii(slider);
            double dr = Math.Abs(r - radius), dsm = Math.Abs(sm - radiusSm), dlg = Math.Abs(lg - radiusLg);
            if (dr <= 0.5 && dsm <= 0.5 && dlg <= 0.5 && dr + dsm + dlg < bestError)
            {
                best = slider;
                bestError = dr + dsm + dlg;
            }
        }

        return best;
    }

    /// <summary>A type-scale size at the given percent, to the nearest half point and never below 8.</summary>
    public static double ScaleFontSize(double size, int percent) =>
        Math.Max(8, Math.Round(size * NormalizeFontScale(percent) / 100.0 * 2, MidpointRounding.AwayFromZero) / 2);

    public static int NormalizeFocusThickness(int thickness) => Math.Clamp(thickness, 0, 2);

    /// <summary>Stroke in pixels for Thin / Normal / Thick.</summary>
    public static double FocusRingStroke(int thickness) => NormalizeFocusThickness(thickness) switch
    {
        0 => 1.5,
        2 => 3,
        _ => 2,
    };

    public static int NormalizeCardElevation(int level) => Math.Clamp(level, 0, 2);

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
