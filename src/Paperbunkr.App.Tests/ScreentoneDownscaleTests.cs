using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Paperbunkr.App.Views;
using SkiaSharp;
using Xunit.Abstractions;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Measures screentone shimmer (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md, #25) at each stage a page passes through: the pipeline's downscale to the viewport width, and the
/// draw-time scale to the final on-screen size. Shimmer is the spread of 8x8 block means over the page's interior: an ideal area-averaged downscale of a regular dot screen is nearly flat, while
/// an aliased one leaves low-frequency beats (moire) that show as blotches and crawl when the page moves.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ScreentoneDownscaleTests
{
    private const int SourceWidth = 2400;
    private const int SourceHeight = 3400;
    private const int ViewportWidth = 1000;
    private readonly ITestOutputHelper _output;

    public ScreentoneDownscaleTests(ITestOutputHelper output) => _output = output;

    /// <summary>A 45-degree dot screen of the given period, hard-edged like a print screentone.</summary>
    internal static SKBitmap Halftone(int width, int height, double period)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var pixels = new uint[width * height];
        double sqrt2 = Math.Sqrt(2);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double u = (x + y) / sqrt2;
                double v = (x - y) / sqrt2;
                double value = Math.Cos(2 * Math.PI * u / period) * Math.Cos(2 * Math.PI * v / period);
                byte gray = value > 0.3 ? (byte)20 : (byte)240;
                pixels[(y * width) + x] = 0xFF000000u | ((uint)gray << 16) | ((uint)gray << 8) | gray;
            }
        }

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.InstallPixels(bitmap.Info, handle.AddrOfPinnedObject(), width * 4);
            return bitmap.Copy();
        }
        finally
        {
            handle.Free();
        }
    }

    private static Bitmap ToAvalonia(SKBitmap sk)
    {
        using var image = SKImage.FromBitmap(sk);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    private static SKBitmap ToSk(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static byte[] GrayOf(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var raw = new uint[size.Width * size.Height];
        var handle = GCHandle.Alloc(raw, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), raw.Length * 4, size.Width * 4);
        }
        finally
        {
            handle.Free();
        }

        bool rgba = bitmap.Format == PixelFormats.Rgba8888;
        var gray = new byte[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            uint p = raw[i];
            uint r = rgba ? p & 0xFF : (p >> 16) & 0xFF;
            uint g = (p >> 8) & 0xFF;
            uint b = rgba ? (p >> 16) & 0xFF : p & 0xFF;
            gray[i] = (byte)((r + g + b) / 3);
        }

        return gray;
    }

    private static byte[] GrayOf(SKBitmap bitmap)
    {
        var gray = new byte[bitmap.Width * bitmap.Height];
        var pixels = bitmap.Pixels;
        for (int i = 0; i < gray.Length; i++)
        {
            gray[i] = (byte)((pixels[i].Red + pixels[i].Green + pixels[i].Blue) / 3);
        }

        return gray;
    }

    /// <summary>The standard deviation of 8x8 block means over the interior (10% margin removed): near zero for a flat, moire-free result.</summary>
    internal static double Shimmer(byte[] gray, int width, int height)
    {
        const int Block = 8;
        int x0 = width / 10, x1 = width - (width / 10), y0 = height / 10, y1 = height - (height / 10);
        var means = new List<double>();
        for (int by = y0; by + Block <= y1; by += Block)
        {
            for (int bx = x0; bx + Block <= x1; bx += Block)
            {
                double sum = 0;
                for (int y = 0; y < Block; y++)
                {
                    for (int x = 0; x < Block; x++)
                    {
                        sum += gray[((by + y) * width) + bx + x];
                    }
                }

                means.Add(sum / (Block * Block));
            }
        }

        double average = means.Average();
        return Math.Sqrt(means.Sum(m => (m - average) * (m - average)) / means.Count);
    }

    private static double ShimmerOf(Bitmap bitmap) => Shimmer(GrayOf(bitmap), bitmap.PixelSize.Width, bitmap.PixelSize.Height);

    private static double ShimmerOf(SKBitmap bitmap) => Shimmer(GrayOf(bitmap), bitmap.Width, bitmap.Height);

    /// <summary>What the compositor draws: the bitmap scaled into a fresh surface with the given sampling.</summary>
    private static SKBitmap DrawScaled(SKBitmap source, int width, int height, SKSamplingOptions sampling)
    {
        var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(target);
        using var image = SKImage.FromBitmap(source);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, new SKRect(0, 0, source.Width, source.Height), new SKRect(0, 0, width, height), sampling, paint);
        return target;
    }

    /// <summary>The ideal: each output pixel is the exact average of the source pixels it covers (a box filter), computed directly.</summary>
    private static SKBitmap BoxScaled(SKBitmap source, int width, int height)
    {
        var src = GrayOf(source);
        var pixels = new SKColor[width * height];
        double sx = source.Width / (double)width, sy = source.Height / (double)height;
        for (int y = 0; y < height; y++)
        {
            int ya = (int)(y * sy), yb = Math.Max(ya + 1, (int)((y + 1) * sy));
            for (int x = 0; x < width; x++)
            {
                int xa = (int)(x * sx), xb = Math.Max(xa + 1, (int)((x + 1) * sx));
                double sum = 0;
                int n = 0;
                for (int yy = ya; yy < yb && yy < source.Height; yy++)
                {
                    for (int xx = xa; xx < xb && xx < source.Width; xx++)
                    {
                        sum += src[(yy * source.Width) + xx];
                        n++;
                    }
                }

                byte v = (byte)(sum / Math.Max(1, n));
                pixels[(y * width) + x] = new SKColor(v, v, v);
            }
        }

        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        result.Pixels = pixels;
        return result;
    }

    [Fact]
    public void MeasureShimmerAtEachStage()
    {
        foreach (double period in new[] { 5.0, 7.0 })
        {
            using var source = Halftone(SourceWidth, SourceHeight, period);
            using var avaloniaSource = ToAvalonia(source);
            int height = (int)Math.Round(SourceHeight * (double)ViewportWidth / SourceWidth);
            _output.WriteLine($"=== screen period {period}px at {SourceWidth}px wide; viewport width {ViewportWidth}px ({SourceWidth / (double)ViewportWidth:F2}x down) ===");

            // Stage (a): the pipeline's downscale (source -> viewport width).
            using var boxViewport = BoxScaled(source, ViewportWidth, height);
            _output.WriteLine($"(a) reference box average                         {ShimmerOf(boxViewport),6:F2}");
            foreach (var mode in new[] { BitmapInterpolationMode.LowQuality, BitmapInterpolationMode.MediumQuality, BitmapInterpolationMode.HighQuality })
            {
                using var scaled = avaloniaSource.CreateScaledBitmap(new PixelSize(ViewportWidth, height), mode);
                _output.WriteLine($"(a) pipeline CreateScaledBitmap {mode,-16}  {ShimmerOf(scaled),6:F2}");
            }

            // Stage (b): what the display bitmap (viewport width, HighQuality) becomes on screen at a smaller size.
            using var displayAvalonia = avaloniaSource.CreateScaledBitmap(new PixelSize(ViewportWidth, height), BitmapInterpolationMode.HighQuality);
            using var displaySk = ToSk(displayAvalonia);                      // the same display bitmap, for the Skia draw tests
            foreach (double drawScale in new[] { 0.83, 0.6 })
            {
                int dw = (int)Math.Round(ViewportWidth * drawScale), dh = (int)Math.Round(height * drawScale);
                using var boxDraw = BoxScaled(source, dw, dh);
                _output.WriteLine($"-- drawn at {drawScale:P0} of the viewport-width bitmap ({dw}px) --");
                _output.WriteLine($"(b) reference box average from source            {ShimmerOf(boxDraw),6:F2}");
                using (var viaAvalonia = displayAvalonia.CreateScaledBitmap(new PixelSize(dw, dh), BitmapInterpolationMode.HighQuality))
                {
                    _output.WriteLine($"(b) paged path today (CreateScaledBitmap High)     {ShimmerOf(viaAvalonia),6:F2}");
                }

                double nearestShimmer;
                using (var nearest = DrawScaled(displaySk, dw, dh, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)))
                {
                    nearestShimmer = ShimmerOf(nearest);
                    _output.WriteLine($"(b) Skia draw, nearest (the bare SKPaint default)  {nearestShimmer,6:F2}");
                }

                using (var linear = DrawScaled(displaySk, dw, dh, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)))
                {
                    _output.WriteLine($"(b) Skia draw, linear                              {ShimmerOf(linear),6:F2}");
                }

                using (var mips = DrawScaled(displaySk, dw, dh, ReaderPageVisualHandler.SamplingFor(highQuality: true, drawWidth: dw, sourceWidth: ViewportWidth)))
                {
                    double mipShimmer = ShimmerOf(mips);
                    _output.WriteLine($"(b) Skia draw, ReaderPageVisualHandler.SamplingFor {mipShimmer,6:F2}");

                    // The finding that drove the fix: the leased-canvas draw (used whenever a colour filter or sharpening is on) sampled nearest-neighbour, which shimmers about twice as much on a screentone.
                    Assert.True(mipShimmer < nearestShimmer * 0.8, $"period {period}, drawn at {drawScale:P0}: mipmapped {mipShimmer:F2} vs nearest {nearestShimmer:F2}");
                }

                using (var cubic = DrawScaled(displaySk, dw, dh, new SKSamplingOptions(SKCubicResampler.Mitchell)))
                {
                    _output.WriteLine($"(b) Skia draw, Mitchell cubic                      {ShimmerOf(cubic),6:F2}");
                }
            }
        }
    }
}
