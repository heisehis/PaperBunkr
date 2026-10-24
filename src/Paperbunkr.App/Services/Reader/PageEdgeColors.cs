using System;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// Ambient letterbox colours (Cosmetics pitch #22, docs/superpowers/specs/2026-10-12-cosmetics-pitch-completion-design.md B).
/// Pure over a small ARGB buffer (the page's thumbnail), so it is unit-testable and costs nothing against the decode budget.
/// Unlike <see cref="Paperbunkr.App.Services.CoverPalette"/> nothing is filtered out: a letterbox should match a white or black page border too.
/// </summary>
public static class PageEdgeColors
{
    /// <summary>Fraction of the page's width/height, on each side, that counts as "the edge".</summary>
    public const double EdgeFraction = 0.06;

    /// <summary>Darkening applied to sampled colours so the page stays the brightest thing on screen.</summary>
    public const double Darken = 0.55;

    public readonly record struct Edges(Color Top, Color Bottom, Color Left, Color Right);

    /// <summary>Average colour of each outer strip of <paramref name="argb"/> (row-major, 0xAARRGGBB). Null when the buffer is empty or the wrong size.</summary>
    public static Edges? FromPixels(ReadOnlySpan<uint> argb, int width, int height)
    {
        if (width <= 0 || height <= 0 || argb.Length < width * height)
        {
            return null;
        }

        int bandY = Math.Max(1, (int)Math.Round(height * EdgeFraction));
        int bandX = Math.Max(1, (int)Math.Round(width * EdgeFraction));
        return new Edges(
            Average(argb, width, 0, bandY, 0, width),
            Average(argb, width, height - bandY, height, 0, width),
            Average(argb, width, 0, height, 0, bandX),
            Average(argb, width, 0, height, width - bandX, width));
    }

    private static Color Average(ReadOnlySpan<uint> argb, int width, int y0, int y1, int x0, int x1)
    {
        long r = 0, g = 0, b = 0, n = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                uint p = argb[(y * width) + x];
                r += (p >> 16) & 0xFF;
                g += (p >> 8) & 0xFF;
                b += p & 0xFF;
                n++;
            }
        }

        return n == 0 ? Colors.Black : Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    /// <summary>Scales a colour towards black by <see cref="Darken"/> and pulls it 15% towards grey so a loud page edge doesn't glare.</summary>
    public static Color Soften(Color color)
    {
        double grey = (color.R + color.G + color.B) / 3.0;
        byte Channel(byte c) => (byte)Math.Clamp(((c * 0.85) + (grey * 0.15)) * Darken, 0, 255);
        return Color.FromRgb(Channel(color.R), Channel(color.G), Channel(color.B));
    }

    /// <summary>
    /// Vertical gradient top-to-bottom, always exactly three stops (so a brush transition between two pages interpolates stop by stop):
    /// the top colour, a middle colour that blends in the left and right edges at 25% each, and the bottom colour.
    /// </summary>
    public static IBrush BuildBrush(Edges edges)
    {
        var top = Soften(edges.Top);
        var bottom = Soften(edges.Bottom);
        var sides = Soften(Mix(edges.Left, edges.Right, 0.5));
        var mid = Mix(Mix(top, bottom, 0.5), sides, 0.5);
        return new ImmutableLinearGradientBrush(
            new[]
            {
                new ImmutableGradientStop(0, top),
                new ImmutableGradientStop(0.5, mid),
                new ImmutableGradientStop(1, bottom),
            },
            startPoint: new Avalonia.RelativePoint(0.5, 0, Avalonia.RelativeUnit.Relative),
            endPoint: new Avalonia.RelativePoint(0.5, 1, Avalonia.RelativeUnit.Relative));
    }

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round((a.R * (1 - t)) + (b.R * t)),
        (byte)Math.Round((a.G * (1 - t)) + (b.G * t)),
        (byte)Math.Round((a.B * (1 - t)) + (b.B * t)));

    /// <summary>Mean of several pages' edges (a double-page spread).</summary>
    public static Edges Average(Edges a, Edges b) => new(Mix(a.Top, b.Top, 0.5), Mix(a.Bottom, b.Bottom, 0.5), Mix(a.Left, b.Left, 0.5), Mix(a.Right, b.Right, 0.5));
}
