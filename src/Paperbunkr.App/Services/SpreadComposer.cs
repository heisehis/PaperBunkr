using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Views;
using SkiaSharp;

namespace Paperbunkr.App.Services;

/// <summary>
/// Stitches two reader pages into one image, exactly as the double-page spread shows them (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md section 2): both pages scaled to
/// the taller page's height (CE's common-height formula, <see cref="SpreadLayoutMath.ComputeCombinedSize"/>), widths summed, no gap; the first page on the left for left-to-right
/// reading and on the right for right-to-left. Which pages pair is the reader's decision; this never decides eligibility.
/// </summary>
public static class SpreadComposer
{
    /// <summary>Where each page goes in the combined image.</summary>
    /// <param name="Canvas">Size of the combined image.</param>
    /// <param name="First">The reading-order first page's slot.</param>
    /// <param name="Second">The reading-order second page's slot.</param>
    public readonly record struct Layout(PixelSize Canvas, PixelRect First, PixelRect Second);

    public static Layout ComputeLayout(PixelSize first, PixelSize second, bool rightToLeft)
    {
        var combined = SpreadLayoutMath.ComputeCombinedSize(first, second);
        int width = combined.Combined.Width;
        int height = combined.Combined.Height;
        int firstWidth = System.Math.Clamp((int)System.Math.Round(width * combined.LeftWidthFraction), 1, System.Math.Max(1, width - 1));
        int secondWidth = width - firstWidth;

        // The reading-order first page sits on the left, or on the right when the book reads right to left.
        return rightToLeft
            ? new Layout(combined.Combined, new PixelRect(secondWidth, 0, firstWidth, height), new PixelRect(0, 0, secondWidth, height))
            : new Layout(combined.Combined, new PixelRect(0, 0, firstWidth, height), new PixelRect(firstWidth, 0, secondWidth, height));
    }

    /// <summary>Composes two Skia bitmaps; the caller owns both inputs and the returned bitmap.</summary>
    public static SKBitmap ComposeSk(SKBitmap first, SKBitmap second, bool rightToLeft)
    {
        var layout = ComputeLayout(new PixelSize(first.Width, first.Height), new PixelSize(second.Width, second.Height), rightToLeft);
        var result = new SKBitmap(layout.Canvas.Width, layout.Canvas.Height);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        DrawInto(canvas, first, layout.First, sampling);
        DrawInto(canvas, second, layout.Second, sampling);
        return result;
    }

    /// <summary>Composes two reader page bitmaps into one new bitmap (the caller disposes it); the inputs are left alone.</summary>
    public static Bitmap Compose(Bitmap first, Bitmap second, bool rightToLeft)
    {
        using var skFirst = ToSk(first);
        using var skSecond = ToSk(second);
        using var stitched = ComposeSk(skFirst, skSecond, rightToLeft);
        using var image = SKImage.FromBitmap(stitched);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    private static void DrawInto(SKCanvas canvas, SKBitmap bitmap, PixelRect slot, SKSamplingOptions sampling)
    {
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, new SKRect(slot.X, slot.Y, slot.X + slot.Width, slot.Y + slot.Height), sampling);
    }

    /// <summary>Round-trips an Avalonia bitmap through PNG into Skia: not the cheapest path, but it needs no pixel-layout assumptions and runs once per copy.</summary>
    private static SKBitmap ToSk(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
