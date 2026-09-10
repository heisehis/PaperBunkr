using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Paperbunkr.App.Services;

/// <summary>
/// Downsamples a cover bitmap to its single average color (docs/superpowers/specs/
/// 2026-09-08-home-navrail-visual-v2-design.md §2/§4) - used to tint the Home masthead scrim toward
/// whichever issue is currently spotlighted. Same SkiaSharp-direct, raw-pixel-copy approach as
/// <see cref="BackdropBlurRenderer"/>/<see cref="CoverWallRenderer"/>, reusing
/// <see cref="BackdropBlurRenderer.ToSkImage"/> rather than a second pixel-copy implementation.
/// </summary>
public static class SpotlightAccentSampler
{
    /// <summary>
    /// Returned when <paramref name="cover"/> has no usable pixels - a neutral mid-gray that blends
    /// into either a dark or light masthead scrim without skewing it either way.
    /// </summary>
    public static readonly Color FallbackColor = Color.FromRgb(0x80, 0x80, 0x80);

    /// <summary>
    /// Scales <paramref name="cover"/> down to a single pixel via an ordinary Skia draw (the
    /// destination-rect scale does the averaging - no separate box-filter pass needed) and returns
    /// that pixel as an <see cref="Avalonia.Media.Color"/>.
    /// </summary>
    public static Color Sample(Bitmap cover)
    {
        var srcSize = cover.PixelSize;
        if (srcSize.Width <= 0 || srcSize.Height <= 0)
        {
            return FallbackColor;
        }

        using SKImage skImage = BackdropBlurRenderer.ToSkImage(cover, srcSize);

        var info = new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.DrawImage(skImage, new SKRect(0, 0, 1, 1));
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        SKColor pixel = pixmap.GetPixelColor(0, 0);

        return Color.FromArgb(pixel.Alpha, pixel.Red, pixel.Green, pixel.Blue);
    }
}
