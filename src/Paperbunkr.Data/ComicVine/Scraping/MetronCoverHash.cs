using System.Globalization;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// The perceptual hash Metron publishes as an issue's <c>cover_hash</c> (docs/superpowers/specs/2026-10-05-
/// metron-api-efficiency-and-matching-design.md section 3) - a port of Python <c>imagehash.phash</c>, which
/// is what Metron's server runs: greyscale, 32x32 Lanczos, a 2-D DCT-II, the 8x8 lowest frequencies, one
/// bit per coefficient above their median, written as 16 hex digits row by row.
///
/// Not <see cref="CoverPerceptualHash"/>: that is CE's average hash, kept for ComicVine so CE's own
/// thresholds still mean what they meant. Nor <c>CoenM.ImageHash.PerceptualHash</c>, which resizes to
/// 64x64 and so produces different bits. A different resampler (ImageSharp's, not Pillow's) moves a
/// bit or two at most, well inside <see cref="MatchDistance"/>.
/// </summary>
public static class MetronCoverHash
{
    /// <summary>Metron-Tagger's own bar (<c>HAMMING_DISTANCE = 10</c>): two covers this close are the same cover.</summary>
    public const int MatchDistance = 10;

    private const int Size = 32;
    private const int LowFrequencies = 8;

    // cos((2x + 1) * u * pi / 64) for x in 0..31, u in 0..7: the only DCT terms the hash reads.
    private static readonly double[,] Cosines = BuildCosines();

    public static ulong? FromFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var image = Image.Load<Rgb24>(filePath);
            return Hash(image);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    public static ulong? FromBytes(byte[]? imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var image = Image.Load<Rgb24>(imageBytes);
            return Hash(image);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Metron's 16-hex-digit form to the same 64 bits; null for anything else (a missing or malformed value).</summary>
    public static ulong? Parse(string? hex) =>
        hex is { Length: 16 } && ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value) ? value : null;

    public static string Format(ulong hash) => hash.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>How many of the 64 bits differ: 0 is identical, around 32 is unrelated.</summary>
    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    public static bool IsMatch(ulong a, ulong b) => Distance(a, b) <= MatchDistance;

    private static ulong Hash(Image<Rgb24> image)
    {
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(Size, Size),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Lanczos3,
        }));

        // Pillow's "L" conversion: ITU-R 601 luma, which is what imagehash feeds its DCT.
        var luma = new double[Size, Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Rgb24 pixel = image[x, y];
                luma[y, x] = Math.Round((pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000.0);
            }
        }

        // Rows first, then columns - separable, and only the 8 lowest frequencies of each are needed.
        // A DCT's scale factor multiplies every coefficient alike, so it can't change which side of
        // the median one falls on and is left out.
        var rows = new double[Size, LowFrequencies];
        for (int y = 0; y < Size; y++)
        {
            for (int u = 0; u < LowFrequencies; u++)
            {
                double sum = 0;
                for (int x = 0; x < Size; x++)
                {
                    sum += luma[y, x] * Cosines[x, u];
                }

                rows[y, u] = sum;
            }
        }

        var low = new double[LowFrequencies * LowFrequencies];
        for (int v = 0; v < LowFrequencies; v++)
        {
            for (int u = 0; u < LowFrequencies; u++)
            {
                double sum = 0;
                for (int y = 0; y < Size; y++)
                {
                    sum += rows[y, u] * Cosines[y, v];
                }

                low[v * LowFrequencies + u] = sum;
            }
        }

        var sorted = (double[])low.Clone();
        Array.Sort(sorted);
        double median = (sorted[31] + sorted[32]) / 2.0;

        ulong hash = 0;
        foreach (double coefficient in low)
        {
            hash = (hash << 1) | (coefficient > median ? 1UL : 0UL);
        }

        return hash;
    }

    private static double[,] BuildCosines()
    {
        var table = new double[Size, LowFrequencies];
        for (int x = 0; x < Size; x++)
        {
            for (int u = 0; u < LowFrequencies; u++)
            {
                table[x, u] = Math.Cos((2 * x + 1) * u * Math.PI / (2 * Size));
            }
        }

        return table;
    }
}
