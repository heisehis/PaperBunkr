using System;
using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Paperbunkr.App.Services;

/// <summary>Which contrast plate a publisher raster is drawn on.</summary>
public enum IconPlate
{
    /// <summary>Opaque image (its own background) - drawn as-is.</summary>
    None,

    /// <summary>Transparent image with dark/coloured content - light plate.</summary>
    Light,

    /// <summary>Transparent image whose visible pixels are light - dark plate.</summary>
    Dark,
}

/// <summary>A decoded publisher raster plus the plate it needs.</summary>
public sealed record PublisherIconImage(Bitmap Bitmap, IconPlate Plate);

/// <summary>
/// Decode + memoise CE's publisher PNG/JPG logos and classify the contrast plate each needs
/// (docs/superpowers/specs/2026-09-25-publisher-icons-and-reader-textures-design.md §A3). The
/// pack is a light-theme set: about 4/5 of the transparent logos are dark, so on a dark UI they
/// would vanish without a plate; the few light-on-transparent ones vanish on a light plate.
/// Decoded bitmaps are immutable and shared, like <see cref="SvgMarkRenderer"/>'s cache.
/// </summary>
public static class PublisherIconBitmaps
{
    /// <summary>More than this fraction of translucent pixels means the logo relies on the
    /// surface behind it (a transparent PNG); at or below it the image carries its own background.</summary>
    internal const double TransparentFractionForPlate = 0.05;

    /// <summary>Mean luminance (0-255) of the visible pixels above which a transparent logo is
    /// treated as "light" and gets the dark plate.</summary>
    internal const double LightLogoLuminance = 170;

    private static readonly ConcurrentDictionary<string, PublisherIconImage?> Cache = new();

    public static PublisherIconImage? Load(string avaresUri) =>
        string.IsNullOrWhiteSpace(avaresUri) ? null : Cache.GetOrAdd(avaresUri, LoadUncached);

    private static PublisherIconImage? LoadUncached(string avaresUri)
    {
        try
        {
            var uri = new Uri(avaresUri);
            if (!AssetLoader.Exists(uri))
            {
                return null;
            }

            Bitmap bitmap;
            using (Stream stream = AssetLoader.Open(uri))
            {
                bitmap = new Bitmap(stream);
            }

            IconPlate plate;
            using (Stream stream = AssetLoader.Open(uri))
            using (SKBitmap? sk = SKBitmap.Decode(stream))
            {
                plate = sk is null ? IconPlate.Light : ClassifyPlate(sk.Pixels);
            }

            return new PublisherIconImage(bitmap, plate);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Pure classifier over unpremultiplied pixels (see the two thresholds above).</summary>
    internal static IconPlate ClassifyPlate(ReadOnlySpan<SKColor> pixels)
    {
        if (pixels.Length == 0)
        {
            return IconPlate.Light;
        }

        // Every 4th pixel is plenty for a mean, and keeps a 512x512 logo cheap.
        int step = pixels.Length > 4096 ? 4 : 1;
        int total = 0, translucent = 0, visible = 0;
        double lumSum = 0;
        for (int i = 0; i < pixels.Length; i += step)
        {
            SKColor p = pixels[i];
            total++;
            if (p.Alpha < 200)
            {
                translucent++;
            }

            if (p.Alpha > 40)
            {
                visible++;
                lumSum += 0.299 * p.Red + 0.587 * p.Green + 0.114 * p.Blue;
            }
        }

        if ((double)translucent / total <= TransparentFractionForPlate)
        {
            return IconPlate.None;
        }

        return visible > 0 && lumSum / visible > LightLogoLuminance ? IconPlate.Dark : IconPlate.Light;
    }
}
