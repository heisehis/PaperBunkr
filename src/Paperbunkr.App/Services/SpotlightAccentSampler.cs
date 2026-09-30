using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Paperbunkr.App.Services;

/// <summary>
/// Picks a cover bitmap's accent colour (docs/superpowers/specs/
/// 2026-09-08-home-navrail-visual-v2-design.md §2/§4) - used to tint the Home masthead scrim toward
/// whichever issue is currently spotlighted. Same SkiaSharp-direct, raw-pixel-copy approach as
/// <see cref="BackdropBlurRenderer"/>/<see cref="CoverWallRenderer"/>, reusing
/// <see cref="BackdropBlurRenderer.ToSkImage"/> rather than a second pixel-copy implementation.
/// </summary>
public static class SpotlightAccentSampler
{
    /// <summary>
    /// Returned when <paramref name="cover"/> has no usable pixels - a neutral mid-gray that blends
    /// into either a dark or light masthead scrim without skewing it either way.
    /// </summary>
    public static readonly Color FallbackColor = Color.FromRgb(0x80, 0x80, 0x80);

    /// <summary>
    /// The cover's dominant vibrant colour (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md C1 - the old one-pixel
    /// average read as muddy brown/grey on most covers): downscales to 16x16 through an ordinary Skia draw, then hands the pixels
    /// to <see cref="PickVibrant"/>.
    /// </summary>
    public static Color Sample(Bitmap cover)
    {
        var srcSize = cover.PixelSize;
        if (srcSize.Width <= 0 || srcSize.Height <= 0)
        {
            return FallbackColor;
        }

        using SKImage skImage = BackdropBlurRenderer.ToSkImage(cover, srcSize);

        const int side = 16;
        var info = new SKImageInfo(side, side, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.DrawImage(skImage, new SKRect(0, 0, side, side));
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        var pixels = new List<Color>(side * side);
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                SKColor p = pixmap.GetPixelColor(x, y);
                pixels.Add(Color.FromRgb(p.Red, p.Green, p.Blue));
            }
        }

        return PickVibrant(pixels);
    }

    /// <summary>
    /// Pure core of <see cref="Sample"/>: buckets pixels into 12 hue bins, weighting each by saturation x value and skipping
    /// near-grey (s &lt; 0.2) and near-black (v &lt; 0.15) pixels, and returns the heaviest bin's weighted mean colour. Falls back to
    /// the plain average when under 10% of the pixels qualify, so a genuinely grey cover still gets a matching grey.
    /// </summary>
    public static Color PickVibrant(IReadOnlyList<Color> pixels)
    {
        if (pixels.Count == 0)
        {
            return FallbackColor;
        }

        const int bins = 12;
        var weight = new double[bins];
        var r = new double[bins];
        var g = new double[bins];
        var b = new double[bins];
        int qualifying = 0;

        foreach (var c in pixels)
        {
            double max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0;
            double min = Math.Min(c.R, Math.Min(c.G, c.B)) / 255.0;
            double v = max;
            double s = max <= 0 ? 0 : (max - min) / max;
            if (s < 0.2 || v < 0.15)
            {
                continue;
            }

            qualifying++;
            double w = s * v;
            int bin = (int)(Hue(c) / 360.0 * bins) % bins;
            weight[bin] += w;
            r[bin] += c.R * w;
            g[bin] += c.G * w;
            b[bin] += c.B * w;
        }

        if (qualifying < pixels.Count * 0.1)
        {
            return Color.FromRgb(
                (byte)Math.Round(pixels.Average(p => p.R)),
                (byte)Math.Round(pixels.Average(p => p.G)),
                (byte)Math.Round(pixels.Average(p => p.B)));
        }

        int best = 0;
        for (int i = 1; i < bins; i++)
        {
            if (weight[i] > weight[best])
            {
                best = i;
            }
        }

        return Color.FromRgb(
            (byte)Math.Round(r[best] / weight[best]),
            (byte)Math.Round(g[best] / weight[best]),
            (byte)Math.Round(b[best] / weight[best]));
    }

    private static double Hue(Color c)
    {
        double rr = c.R / 255.0, gg = c.G / 255.0, bb = c.B / 255.0;
        double max = Math.Max(rr, Math.Max(gg, bb));
        double min = Math.Min(rr, Math.Min(gg, bb));
        double d = max - min;
        if (d <= 0)
        {
            return 0;
        }

        double h = max == rr ? (gg - bb) / d % 6 : max == gg ? (bb - rr) / d + 2 : (rr - gg) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }
}
