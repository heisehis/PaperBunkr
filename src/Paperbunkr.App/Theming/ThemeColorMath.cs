using System;
using Avalonia.Media;

namespace Paperbunkr.App.Theming;

/// <summary>
/// The colour arithmetic the theme engine uses (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 1a): WCAG
/// contrast, contrast-seeking adjustment, mixing. Moved out of <c>ThemeService</c> unchanged so the resolver can use it without an application.
/// Only the <see cref="Color"/> value type is touched here - no brushes, no resources, no dispatcher.
/// </summary>
public static class ThemeColorMath
{
    public static double RelativeLuminance(Color c)
    {
        static double Lin(byte channel)
        {
            double v = channel / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Lin(c.R)) + (0.7152 * Lin(c.G)) + (0.0722 * Lin(c.B));
    }

    /// <summary>WCAG 2.1 contrast ratio (1..21). Alpha is ignored, as it always has been here.</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        double l1 = RelativeLuminance(a);
        double l2 = RelativeLuminance(b);
        if (l2 > l1)
        {
            (l1, l2) = (l2, l1);
        }

        return (l1 + 0.05) / (l2 + 0.05);
    }

    public static bool IsLight(Color c) => RelativeLuminance(c) > 0.5;

    /// <summary>
    /// Moves <paramref name="fg"/> toward whichever direction increases contrast against <paramref name="bg"/> until it reaches
    /// <paramref name="targetRatio"/> - darker on a light background, lighter on a dark one. The direction is decided from the background, never the
    /// foreground (a fixed "always darken" rule was wrong for every dark theme). Byte-for-byte the algorithm <c>ThemeService</c> shipped with.
    /// </summary>
    public static Color AdjustForContrast(Color fg, Color bg, double targetRatio)
    {
        if (ContrastRatio(fg, bg) >= targetRatio)
        {
            return fg;
        }

        bool bgIsLight = IsLight(bg);
        Color current = fg;
        for (int i = 0; i < 20; i++)
        {
            current = bgIsLight
                ? Color.FromRgb((byte)(current.R * 0.9), (byte)(current.G * 0.9), (byte)(current.B * 0.9))
                : Color.FromRgb(
                    (byte)Math.Min(255, current.R + ((255 - current.R) * 0.12)),
                    (byte)Math.Min(255, current.G + ((255 - current.G) * 0.12)),
                    (byte)Math.Min(255, current.B + ((255 - current.B) * 0.12)));

            if (ContrastRatio(current, bg) >= targetRatio)
            {
                return current;
            }
        }

        // 20 steps were not enough (a hue that cannot reach the target without going to an extreme) - the safest end in the right direction.
        return bgIsLight ? Colors.Black : Colors.White;
    }

    /// <summary>Linear blend: 0 = <paramref name="a"/>, 1 = <paramref name="b"/>. The result is opaque unless both inputs share an alpha.</summary>
    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        static byte Lerp(byte x, byte y, double t) => (byte)Math.Round(x + ((y - x) * t));
        return Color.FromArgb(Lerp(a.A, b.A, t), Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));
    }

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);

    public static Color Lighten(Color c, double amount) => Mix(c, Color.FromArgb(c.A, 255, 255, 255), amount);

    public static Color Darken(Color c, double amount) => Mix(c, Color.FromArgb(c.A, 0, 0, 0), amount);

    /// <summary>Whichever of a near-black and a near-white reads better on <paramref name="background"/> - for text placed on a filled colour.</summary>
    public static Color ReadableOn(Color background, Color dark, Color light) =>
        ContrastRatio(dark, background) >= ContrastRatio(light, background) ? dark : light;

    public static string ToHex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Parses <c>#RGB</c>, <c>#ARGB</c>, <c>#RRGGBB</c>, <c>#AARRGGBB</c> and Avalonia's named colours. False for anything else, never throws.</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        return !string.IsNullOrWhiteSpace(text) && Color.TryParse(text.Trim(), out color);
    }
}
