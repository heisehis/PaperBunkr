using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using SixLabors.ImageSharp;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// Cover-hash confirmation for auto-match (docs/superpowers/specs/2026-09-24-comicvine-scraper-
/// fidelity-design.md §2.1) - CE's real <c>imagehash.py</c> is a plain average hash (verified directly
/// against the extracted plugin source: resize to 8x8, luma-weighted greyscale, one bit per pixel
/// above the mean, Hamming-distance similarity), so this uses <see cref="AverageHash"/> specifically
/// from <c>CoenM.ImageSharp.ImageHash</c>, not its DCT-based <c>PerceptualHash</c>, keeping computed
/// values comparable to CE's own verified 0.87/0.77 thresholds. Normalizes the library's 0-100
/// percentage scale to CE's own 0.0-1.0 convention (<c>CompareHash.Similarity</c> returns a 0-100
/// percentage, confirmed directly against its source - not 0.0-1.0 like CE's <c>similarity()</c>).
/// </summary>
public static class CoverPerceptualHash
{
    private static readonly AverageHash Algorithm = new();

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
            return Algorithm.Hash(stream);
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
            return Algorithm.Hash(stream);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>0.0 (very different) to 1.0 (identical) - CE's own convention, normalized from the library's 0-100 percentage scale.</summary>
    public static double Similarity(ulong hash1, ulong hash2) => CompareHash.Similarity(hash1, hash2) / 100.0;
}
