using Paperbunkr.App.Services.AdDetection;
using SkiaSharp;

namespace Paperbunkr.App.Tests;

/// <summary>dHash behaviour on synthetic bitmaps (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md §5).</summary>
public class PageHasherTests
{
    /// <summary>A deterministic page: a coarse grid of random grey blocks, so the hash has real structure. Different seeds are unrelated pages.</summary>
    internal static SKBitmap Pattern(int seed, int width = 240, int height = 320)
    {
        var random = new Random(seed);
        const int cols = 12;
        const int rows = 16;
        var levels = new byte[cols * rows];
        random.NextBytes(levels);

        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                byte v = levels[r * cols + c];
                paint.Color = new SKColor(v, v, v);
                canvas.DrawRect(c * (width / (float)cols), r * (height / (float)rows), width / (float)cols + 1, height / (float)rows + 1, paint);
            }
        }

        return bitmap;
    }

    internal static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 90)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    private static SKBitmap Rescale(SKBitmap source, float factor)
    {
        var info = new SKImageInfo((int)(source.Width * factor), (int)(source.Height * factor));
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))!;
    }

    [Fact]
    public void Compute_SameBitmapTwice_IsIdentical()
    {
        using var page = Pattern(1);

        Assert.Equal(PageHasher.Compute(page), PageHasher.Compute(page));
        Assert.Equal(0, PageHasher.Distance(PageHasher.Compute(page), PageHasher.Compute(page)));
    }

    [Fact]
    public void Compute_RecompressedJpegCopy_IsCloserThanSixBits()
    {
        using var page = Pattern(2);
        long original = PageHasher.Compute(page);

        long? copy = PageHasher.TryCompute(Encode(page, SKEncodedImageFormat.Jpeg, quality: 55));

        Assert.NotNull(copy);
        Assert.True(PageHasher.Distance(original, copy!.Value) <= 6, $"distance was {PageHasher.Distance(original, copy.Value)}");
    }

    [Fact]
    public void Compute_RescaledCopy_IsCloserThanSixBits()
    {
        using var page = Pattern(3);
        using var smaller = Rescale(page, 0.6f);

        int distance = PageHasher.Distance(PageHasher.Compute(page), PageHasher.Compute(smaller));
        Assert.True(distance <= 6, $"distance was {distance}");
    }

    [Fact]
    public void Compute_UnrelatedPage_IsFarAway()
    {
        using var a = Pattern(4);
        using var b = Pattern(5);

        int distance = PageHasher.Distance(PageHasher.Compute(a), PageHasher.Compute(b));
        Assert.True(distance > 6, $"distance was {distance}");
    }

    [Fact]
    public void Compute_FlatImage_IsDeterministicAndDoesNotThrow()
    {
        using var flat = new SKBitmap(50, 80);
        flat.Erase(SKColors.SteelBlue);

        Assert.Equal(0, PageHasher.Compute(flat));
    }

    [Fact]
    public void Compute_TransparentPage_HashesAsPaper()
    {
        using var clear = new SKBitmap(50, 80);
        clear.Erase(SKColors.Transparent);
        using var white = new SKBitmap(50, 80);
        white.Erase(SKColors.White);

        Assert.Equal(PageHasher.Compute(white), PageHasher.Compute(clear));
    }

    [Fact]
    public void TryCompute_Garbage_ReturnsNull()
    {
        Assert.Null(PageHasher.TryCompute(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Null(PageHasher.TryCompute((byte[]?)null));
        Assert.Null(PageHasher.TryCompute(Array.Empty<byte>()));
    }

    [Fact]
    public void Distance_CountsDifferingBits_IncludingTheSignBit()
    {
        Assert.Equal(0, PageHasher.Distance(5, 5));
        Assert.Equal(1, PageHasher.Distance(0, 1));
        Assert.Equal(64, PageHasher.Distance(0, -1));
        Assert.Equal(1, PageHasher.Distance(long.MinValue, 0));
    }
}
