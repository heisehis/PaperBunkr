using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Paperbunkr.App.Services;

/// <summary>
/// Dominant-colour extraction from a cover (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #9). Pure over a
/// small pixel buffer so it is unit-testable; <see cref="FromBitmap"/> samples a decoded cover on a 24x24 grid first, so it costs
/// well under a millisecond and needs no persistence (a deviation from the spec's planned database column, recorded in the
/// spec's implementation notes). Greyscale/near-black/near-white pixels are ignored so a mostly-dark or paper-white cover still
/// yields its actual colours; if nothing colourful remains the list is empty and callers fall back to the skin accent.
/// </summary>
public static class CoverPalette
{
    public const int MaxColors = 3;
    private const int Side = 24;

    /// <summary>Up to <see cref="MaxColors"/> dominant, mutually distinct colours from ARGB (0xAARRGGBB) pixels, most dominant first.</summary>
    public static IReadOnlyList<Color> FromPixels(ReadOnlySpan<uint> argb)
    {
        var buckets = new Dictionary<int, (double W, double R, double G, double B)>();
        foreach (uint p in argb)
        {
            if ((p >> 24) < 128)
            {
                continue;
            }

            int r = (int)((p >> 16) & 0xFF), g = (int)((p >> 8) & 0xFF), b = (int)(p & 0xFF);
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double v = max / 255.0;
            double s = max == 0 ? 0 : (max - min) / (double)max;
            if (v < 0.18 || s < 0.22)
            {
                continue; // near-black, near-white/grey: carries no accent information
            }

            double weight = 0.25 + s;
            int key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
            buckets.TryGetValue(key, out var acc);
            buckets[key] = (acc.W + weight, acc.R + (r * weight), acc.G + (g * weight), acc.B + (b * weight));
        }

        var picked = new List<Color>();
        foreach (var (_, acc) in buckets.OrderByDescending(kv => kv.Value.W))
        {
            var color = Color.FromRgb((byte)(acc.R / acc.W), (byte)(acc.G / acc.W), (byte)(acc.B / acc.W));
            if (picked.All(c => Distance(c, color) > 70))
            {
                picked.Add(color);
                if (picked.Count == MaxColors)
                {
                    break;
                }
            }
        }

        return picked;
    }

    /// <summary>Palette of a decoded cover; empty on any failure (a bad bitmap must never break a screen).</summary>
    public static IReadOnlyList<Color> FromBitmap(Bitmap? cover)
    {
        if (cover is null)
        {
            return Array.Empty<Color>();
        }

        try
        {
            // Copy the decoded pixels and sample a Side x Side grid from them. (Not CreateScaledBitmap: it needs a Skia-backed bitmap
            // and throws for other bitmap types; a cover thumbnail is small enough that one straight copy is cheap.)
            int w = cover.PixelSize.Width, h = cover.PixelSize.Height;
            if (w <= 0 || h <= 0)
            {
                return Array.Empty<Color>();
            }

            var all = new uint[w * h];
            var handle = GCHandle.Alloc(all, GCHandleType.Pinned);
            try
            {
                cover.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), all.Length * 4, w * 4);
            }
            finally
            {
                handle.Free();
            }

            bool rgba = cover.Format == PixelFormats.Rgba8888;
            var samples = new uint[Side * Side];
            for (int gy = 0; gy < Side; gy++)
            {
                int y = Math.Min(h - 1, (int)((gy + 0.5) * h / Side));
                for (int gx = 0; gx < Side; gx++)
                {
                    int x = Math.Min(w - 1, (int)((gx + 0.5) * w / Side));
                    uint p = all[(y * w) + x];
                    if (rgba)
                    {
                        // Bytes are R,G,B,A in memory; as a little-endian uint that is 0xAABBGGRR - swap R and B into 0xAARRGGBB.
                        p = (p & 0xFF00FF00) | ((p & 0xFF) << 16) | ((p >> 16) & 0xFF);
                    }

                    samples[(gy * Side) + gx] = p;
                }
            }

            return FromPixels(samples);
        }
        catch (Exception)
        {
            return Array.Empty<Color>();
        }
    }

    private static double Distance(Color a, Color b)
    {
        double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }
}
