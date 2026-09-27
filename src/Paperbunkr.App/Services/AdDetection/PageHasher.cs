using System.IO;
using System.Numerics;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Paperbunkr.App.Services.AdDetection;

/// <summary>
/// 64-bit difference hash (dHash) of a comic page (docs/superpowers/specs/2026-09-21-comic-reader-page-
/// intelligence-design.md §5, pitch #9). The page is scaled to 9x8 pixels; each of the 8 rows contributes 8 bits,
/// one per pair of horizontally neighbouring pixels (bit set when the left pixel is brighter than the right). Two
/// copies of the same page - recompressed, rescaled, slightly cropped - land within a few bits of each other, while
/// unrelated pages differ by around half of the 64. No new dependency: SkiaSharp already ships with Avalonia.
/// </summary>
public static class PageHasher
{
    public const int HashWidth = 9;
    public const int HashHeight = 8;

    /// <summary>Hamming distance between two hashes (0 = identical, 64 = every bit differs).</summary>
    public static int Distance(long a, long b) => BitOperations.PopCount((ulong)(a ^ b));

    /// <summary>The hash of an already-decoded page.</summary>
    public static long Compute(SKBitmap source)
    {
        // Two stages: Skia scales the page down to 8x8 samples per hash cell, then the cell brightness is the exact
        // average of those samples. A single direct 9x8 scale samples too sparsely - near-equal neighbouring cells
        // flip bits between a page and its own rescaled copy.
        const int Oversample = 8;
        const int Width = HashWidth * Oversample;
        const int Height = HashHeight * Oversample;

        using var image = SKImage.FromBitmap(source);
        using var scaled = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(scaled))
        {
            // White underneath, so a transparent page hashes as paper rather than black.
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, new SKRect(0, 0, Width, Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        var cells = new double[HashWidth, HashHeight];
        for (int cy = 0; cy < HashHeight; cy++)
        {
            for (int cx = 0; cx < HashWidth; cx++)
            {
                double sum = 0;
                for (int y = 0; y < Oversample; y++)
                {
                    for (int x = 0; x < Oversample; x++)
                    {
                        sum += Luminance(scaled.GetPixel(cx * Oversample + x, cy * Oversample + y));
                    }
                }

                cells[cx, cy] = sum / (Oversample * Oversample);
            }
        }

        ulong bits = 0;
        int bit = 0;
        for (int y = 0; y < HashHeight; y++)
        {
            for (int x = 0; x < HashWidth - 1; x++)
            {
                if (cells[x, y] > cells[x + 1, y])
                {
                    bits |= 1UL << bit;
                }

                bit++;
            }
        }

        return (long)bits;
    }

    /// <summary>The hash of encoded image bytes (JPEG, PNG, ...), or <see langword="null"/> when Skia cannot decode them.</summary>
    public static long? TryCompute(byte[]? encoded)
    {
        if (encoded is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var bitmap = SKBitmap.Decode(encoded);
            return bitmap is null ? null : Compute(bitmap);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The hash of an Avalonia bitmap - the fallback for formats the engine decodes itself (WebP, HEIF, ...). Round-trips through PNG, which is fine for a scan that touches a handful of pages per file.</summary>
    public static long? TryCompute(Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return TryCompute(stream.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static double Luminance(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
}
