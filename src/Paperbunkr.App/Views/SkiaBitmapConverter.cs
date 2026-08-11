using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Paperbunkr.App.Views;

/// <summary>
/// Converts an already-decoded Avalonia <see cref="Bitmap"/> into a raw <see cref="SKImage"/> for
/// direct drawing through a leased <c>Avalonia.Skia.ISkiaSharpApiLease.SkCanvas</c> (docs/
/// superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §9) -
/// needed because live image adjustment applies an <see cref="SKColorFilter"/>-carrying
/// <see cref="SKPaint"/> at draw time, and Avalonia's own <c>Bitmap</c>/
/// <c>ImmediateDrawingContext.DrawBitmap</c> has no public hook for a custom paint (confirmed: mixing
/// a leased canvas's own draw calls with Avalonia's higher-level <c>DrawBitmap</c> in the same
/// render pass produced a blank frame in practice - the two are separate rendering strategies, not
/// meant to be combined). Avalonia's underlying Skia bitmap wrapper
/// (<c>Avalonia.Skia.ImmutableBitmap</c>) is <c>internal</c> to the Avalonia.Skia assembly, so there
/// is no supported way to reach its <see cref="SKImage"/> directly either - this goes through the one
/// public pixel-level API Avalonia does expose (<see cref="WriteableBitmap"/>/
/// <see cref="ILockedFramebuffer"/>/<see cref="Bitmap.CopyPixels(ILockedFramebuffer)"/>) instead. A
/// raw pixel copy, not a per-pixel transform, so it's cheap relative to the color-matrix work it
/// enables - the actual brightness/contrast/saturation/gamma math stays entirely paint-level.
/// </summary>
internal static class SkiaBitmapConverter
{
    public static SKImage ToSkImage(Bitmap source)
    {
        var pixelSize = source.PixelSize;
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
