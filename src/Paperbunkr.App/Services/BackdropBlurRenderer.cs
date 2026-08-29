using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Paperbunkr.App.Services;

/// <summary>
/// Pre-renders a blurred, edge-to-edge-filling backdrop from a cover bitmap, once, in C# - not a
/// live Avalonia <c>BlurEffect</c> in the manga detail header's layout (docs/superpowers/specs/
/// 2026-08-23-manga-detail-screen-design.md). Two earlier attempts at a live Effect (directly on
/// an <c>Image</c>, then on a <c>Border</c>'s <c>ImageBrush</c> background) both only blurred a
/// small square instead of filling the banner - a known Avalonia issue (AvaloniaUI/Avalonia#11416),
/// and a plain <see cref="RenderTargetBitmap"/> snapshot of a detached, Effect-carrying control
/// reproduced the exact same bug rather than avoiding it. Draws directly through SkiaSharp instead
/// (already a transitive dependency via Avalonia.Skia, and already used this way elsewhere in this
/// codebase - <c>Paperbunkr.App.Views.SkiaBitmapConverter</c>), entirely bypassing Avalonia's own
/// Effect/Compositor pipeline rather than fighting it a third time.
/// </summary>
public static class BackdropBlurRenderer
{
    /// <summary>
    /// Crops/scales <paramref name="source"/> to fill <paramref name="targetSize"/> (matching
    /// <c>Stretch="UniformToFill"</c> semantics) and blurs it via <see cref="SKImageFilter.CreateBlur"/>
    /// - a real Gaussian blur, not an approximation. Result is a plain already-blurred bitmap; the
    /// caller's own XAML just needs an ordinary <c>Image</c> with no further Effect.
    /// </summary>
    public static Bitmap? Render(Bitmap source, PixelSize targetSize, float blurSigma = 30f)
    {
        var srcSize = source.PixelSize;
        if (srcSize.Width <= 0 || srcSize.Height <= 0 || targetSize.Width <= 0 || targetSize.Height <= 0)
        {
            return null;
        }

        using SKImage skImage = ToSkImage(source, srcSize);

        var info = new SKImageInfo(targetSize.Width, targetSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // UniformToFill: scale so both dimensions cover the target, centered - same crop
        // semantics as the plain Image Stretch every cover thumbnail in this app already uses.
        float scale = Math.Max((float)targetSize.Width / srcSize.Width, (float)targetSize.Height / srcSize.Height);
        float drawWidth = srcSize.Width * scale;
        float drawHeight = srcSize.Height * scale;
        var destRect = new SKRect(
            (targetSize.Width - drawWidth) / 2f,
            (targetSize.Height - drawHeight) / 2f,
            (targetSize.Width + drawWidth) / 2f,
            (targetSize.Height + drawHeight) / 2f);

        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(blurSigma, blurSigma, SKShaderTileMode.Clamp) };
        canvas.DrawImage(skImage, destRect, paint);
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        byte[] pixelBytes = pixmap.GetPixelSpan().ToArray();

        var writeable = new WriteableBitmap(targetSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = writeable.Lock())
        {
            Marshal.Copy(pixelBytes, 0, fb.Address, pixelBytes.Length);
        }

        return writeable;
    }

    /// <summary>Same raw-pixel-copy technique as <c>Paperbunkr.App.Views.SkiaBitmapConverter.ToSkImage</c>,
    /// inlined here rather than shared with Views, to avoid a Services-depends-on-Views layering
    /// wrinkle. <c>internal</c> so <see cref="CoverWallRenderer"/> (same layer) can reuse it.</summary>
    internal static SKImage ToSkImage(Bitmap source, PixelSize pixelSize)
    {
        int width = pixelSize.Width;
        int height = pixelSize.Height;
        int stride = width * 4;
        int length = stride * height;

        var buffer = new byte[length];
        using (var readBitmap = new WriteableBitmap(pixelSize, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque))
        {
            using var framebuffer = readBitmap.Lock();
            source.CopyPixels(framebuffer);
            Marshal.Copy(framebuffer.Address, buffer, 0, length);
        }

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        return SKImage.FromPixels(info, SKData.CreateCopy(buffer), stride);
    }
}
