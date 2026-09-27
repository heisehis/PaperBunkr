using Paperbunkr.Data.ComicVine.Scraping;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>
/// Table-driven tests for <see cref="CoverPerceptualHash"/> (docs/superpowers/specs/2026-09-24-
/// comicvine-scraper-fidelity-design.md §2.1) against small in-memory fixture images - no live network,
/// no file system beyond a temp file for the file-path overload.
/// </summary>
public sealed class CoverPerceptualHashTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private string WriteTempImage(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_coverhash_test_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, bytes);
        _tempFiles.Add(path);
        return path;
    }

    // Average hash encodes each pixel's brightness relative to the WHOLE image's own average, not
    // absolute color - two solid-color images of any color produce near-identical (arbitrary tie-break)
    // bit patterns, since virtually every pixel equals the average either way. Fixtures need real
    // structure (a genuine light/dark split) for the hash to encode anything meaningful.

    /// <summary>Top half light, bottom half dark - a real structural pattern, not a flat color.</summary>
    private static byte[] TopLightBottomDark(int size = 64)
    {
        using var image = new Image<Rgba32>(size, size);
        var light = new Rgba32(240, 240, 240);
        var dark = new Rgba32(15, 15, 15);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                image[x, y] = y < size / 2 ? light : dark;
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>Same top-light/bottom-dark pattern with a small corner perturbed - similar to, but not identical to, <see cref="TopLightBottomDark"/>.</summary>
    private static byte[] TopLightBottomDarkWithCorner(int size = 64)
    {
        using var image = new Image<Rgba32>(size, size);
        var light = new Rgba32(240, 240, 240);
        var dark = new Rgba32(15, 15, 15);
        var corner = new Rgba32(200, 200, 200);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                image[x, y] = x < 4 && y < 4 ? corner : (y < size / 2 ? light : dark);
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>The exact tonal inverse of <see cref="TopLightBottomDark"/> (bottom light, top dark) - every pixel's above/below-average bit flips, giving a robust "maximally different" fixture rather than a near-50%-overlap edge case.</summary>
    private static byte[] BottomLightTopDark(int size = 64)
    {
        using var image = new Image<Rgba32>(size, size);
        var light = new Rgba32(240, 240, 240);
        var dark = new Rgba32(15, 15, 15);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                image[x, y] = y < size / 2 ? dark : light;
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    [Fact]
    public void HashFromBytes_NearIdenticalImages_ScoreHighSimilarity()
    {
        ulong? hash1 = CoverPerceptualHash.HashFromBytes(TopLightBottomDark());
        ulong? hash2 = CoverPerceptualHash.HashFromBytes(TopLightBottomDarkWithCorner());

        Assert.NotNull(hash1);
        Assert.NotNull(hash2);
        Assert.True(CoverPerceptualHash.Similarity(hash1.Value, hash2.Value) > 0.87);
    }

    [Fact]
    public void HashFromBytes_VeryDifferentImages_ScoreLowSimilarity()
    {
        ulong? topLight = CoverPerceptualHash.HashFromBytes(TopLightBottomDark());
        ulong? bottomLight = CoverPerceptualHash.HashFromBytes(BottomLightTopDark());

        Assert.NotNull(topLight);
        Assert.NotNull(bottomLight);
        // The tonal inverse flips virtually every above/below-average bit - similarity should be near 0,
        // nowhere close to the 0.87/0.77 auto-match thresholds this hash gates.
        Assert.True(CoverPerceptualHash.Similarity(topLight.Value, bottomLight.Value) < 0.3);
    }

    [Fact]
    public void HashFromBytes_IdenticalImages_AreExactlyOne()
    {
        byte[] bytes = TopLightBottomDark();
        ulong? hash1 = CoverPerceptualHash.HashFromBytes(bytes);
        ulong? hash2 = CoverPerceptualHash.HashFromBytes(bytes);

        Assert.Equal(1.0, CoverPerceptualHash.Similarity(hash1!.Value, hash2!.Value));
    }

    [Fact]
    public void HashFromBytes_GarbageBytes_ReturnsNull()
    {
        Assert.Null(CoverPerceptualHash.HashFromBytes(new byte[] { 1, 2, 3, 4 }));
        Assert.Null(CoverPerceptualHash.HashFromBytes(null));
        Assert.Null(CoverPerceptualHash.HashFromBytes(Array.Empty<byte>()));
    }

    [Fact]
    public void HashFromFile_MissingFile_ReturnsNull()
    {
        Assert.Null(CoverPerceptualHash.HashFromFile(null));
        Assert.Null(CoverPerceptualHash.HashFromFile(""));
        Assert.Null(CoverPerceptualHash.HashFromFile(@"C:\this\does\not\exist.png"));
    }

    [Fact]
    public void HashFromFile_RealImage_MatchesHashFromBytes()
    {
        byte[] bytes = TopLightBottomDark();
        string path = WriteTempImage(bytes);

        ulong? fromFile = CoverPerceptualHash.HashFromFile(path);
        ulong? fromBytes = CoverPerceptualHash.HashFromBytes(bytes);

        Assert.Equal(fromBytes, fromFile);
    }
}
