using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// Cover-hash confirmation for auto-match (docs/superpowers/specs/2026-09-24-comicvine-scraper-
/// fidelity-design.md §2.1) - CE's real <c>imagehash.py</c> is a plain average hash (verified directly
/// against the extracted plugin source: resize to 8x8, luma-weighted greyscale, one bit per pixel
/// above the mean, Hamming-distance similarity), matching CE's verified 0.87/0.77 thresholds.
/// </summary>
public static class CoverPerceptualHash
{
    private const int HashSize = 8;
    private const int PixelCount = HashSize * HashSize;

    /// <summary>The perceptual hash of the image at <paramref name="filePath"/>, or null if it can't be
    /// read/decoded - a missing or corrupt cover skips the gate entirely rather than half-computing it.</summary>
    public static ulong? HashFromFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            return Hash(stream);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The perceptual hash of image bytes fetched over the network (a candidate's cover URL), or null if they can't be decoded.</summary>
    public static ulong? HashFromBytes(byte[]? imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(imageBytes);
            return Hash(stream);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>0.0 (very different) to 1.0 (identical) - CE's own convention.</summary>
    public static double Similarity(ulong hash1, ulong hash2) => 1.0 - BitOperations.PopCount(hash1 ^ hash2) / (double)PixelCount;

    private static ulong Hash(Stream stream)
    {
        using var image = Image.Load<Rgba32>(stream);
        image.Mutate(context => context
            .Resize(HashSize, HashSize)
            .Grayscale(GrayscaleMode.Bt601)
            .AutoOrient());

        uint average = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < HashSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < HashSize; x++)
                {
                    average += row[x].R;
                }
            }
        });
        average /= PixelCount;

        ulong hash = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < HashSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < HashSize; x++)
                {
                    hash <<= 1;
                    if (row[x].R >= average)
                    {
                        hash |= 1;
                    }
                }
            }
        });

        return hash;
    }
}
