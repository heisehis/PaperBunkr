using System;
using Avalonia.Media;
using Paperbunkr.App.Theming;

namespace Paperbunkr.App.Services;

/// <summary>
/// Pure colour helpers for the Edit Collection dialog's custom-accent flyout
/// (docs/superpowers/specs/2026-11-06-collection-dialog-polish-design.md): hex entry, HSV round-trip for the picker area, and the
/// advisory low-contrast check. No brushes, no resources - only the <see cref="Color"/> value type, so it is testable without an Application.
/// </summary>
public static class AccentColorInput
{
    /// <summary>Contrast below which the dialog shows its advisory hint (WCAG's threshold for non-text UI).</summary>
    public const double LowContrastThreshold = 3.0;

    /// <summary>
    /// Accepts <c>#RGB</c> / <c>#RRGGBB</c> with or without the leading <c>#</c>. Anything else (named colours, alpha forms, junk)
    /// is rejected - an accent is always an opaque <c>#RRGGBB</c> in the database.
    /// </summary>
    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim().TrimStart('#');
        if (trimmed.Length is not (3 or 6))
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        if (!ThemeColorMath.TryParse("#" + trimmed, out var parsed))
        {
            return false;
        }

        color = ThemeColorMath.Opaque(parsed);
        return true;
    }

    /// <summary>Upper-case opaque <c>#RRGGBB</c>.</summary>
    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>True when <paramref name="accent"/> reads below <see cref="LowContrastThreshold"/>:1 against <paramref name="surface"/>.</summary>
    public static bool IsLowContrast(Color accent, Color surface) =>
        ThemeColorMath.ContrastRatio(accent, surface) < LowContrastThreshold;

    /// <summary>Hue 0..360, saturation and value 0..1.</summary>
    public static (double H, double S, double V) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h;
        if (delta == 0)
        {
            h = 0;
        }
        else if (max == r)
        {
            h = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            h = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            h = 60 * (((r - g) / delta) + 4);
        }

        if (h < 0)
        {
            h += 360;
        }

        double s = max == 0 ? 0 : delta / max;
        return (h, s, max);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);

        double c = v * s;
        double x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        double m = v - c;

        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
