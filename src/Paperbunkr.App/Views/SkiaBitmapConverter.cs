using System;
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

    /// <summary>
    /// The reverse direction of <see cref="ToSkImage"/> - a raw <see cref="SKBitmap"/> (decoded
    /// straight off a <c>SkiaSharp.SKCodec</c> scanline session, docs/superpowers/specs/2026-09-09-
    /// reader-webtoon-strip-band-decode-design.md §4.1) into a real Avalonia <see cref="Bitmap"/>.
    /// Avalonia has no public "wrap this SKBitmap" API (same reason <see cref="ToSkImage"/> can't go
    /// the other way through Avalonia's own internal Skia bitmap wrapper) - goes through the same
    /// public pixel-level surface, <see cref="WriteableBitmap"/>/<see cref="ILockedFramebuffer"/>,
    /// just copying pixels in instead of out. Matches <paramref name="source"/>'s own
    /// <see cref="SKBitmap.ColorType"/>/<see cref="SKBitmap.AlphaType"/> exactly rather than
    /// assuming a fixed one - <see cref="StripDecodeSession"/> decodes in whatever native format the
    /// codec reports (an earlier draft assumed <see cref="SKColorType.Rgba8888"/>/
    /// <see cref="SKAlphaType.Opaque"/> always, which real JPEG decode turned out to report as
    /// <see cref="SKColorType.Bgra8888"/> on this platform - forcing a mismatched target made
    /// scanline decode itself fail with <c>InvalidConversion</c>), so this conversion has to follow
    /// suit rather than assume.
    /// </summary>
    public static WriteableBitmap FromSkBitmap(SKBitmap source)
    {
        var pixelSize = new PixelSize(source.Width, source.Height);
        var pixelFormat = source.ColorType switch
        {
            SKColorType.Bgra8888 => PixelFormat.Bgra8888,
            SKColorType.Rgba8888 => PixelFormat.Rgba8888,
            _ => throw new NotSupportedException($"FromSkBitmap: unsupported SKColorType '{source.ColorType}' - only Bgra8888/Rgba8888 are wired up (both are 4-byte, byte-for-byte-mappable to an Avalonia PixelFormat; anything else would need a per-pixel conversion this method deliberately doesn't do).")
        };
        var alphaFormat = source.AlphaType switch
        {
            SKAlphaType.Opaque => AlphaFormat.Opaque,
            SKAlphaType.Premul => AlphaFormat.Premul,
            SKAlphaType.Unpremul => AlphaFormat.Unpremul,
            _ => AlphaFormat.Opaque
        };
        var writeable = new WriteableBitmap(pixelSize, new Vector(96, 96), pixelFormat, alphaFormat);

        using var framebuffer = writeable.Lock();
        int sourceRowBytes = source.RowBytes;
        int destRowBytes = framebuffer.RowBytes;
        IntPtr sourcePixels = source.GetPixels();

        if (sourceRowBytes == destRowBytes)
        {
            // Fast path: same assumption ToSkImage's own bulk copy already relies on in this
            // codebase - on this app's actual Skia/Win32 backend, a standard 4-byte-per-pixel
            // format's row stride isn't padded, so source and destination line up as one big copy.
            int length = sourceRowBytes * source.Height;
            var buffer = new byte[length];
            Marshal.Copy(sourcePixels, buffer, 0, length);
            Marshal.Copy(buffer, 0, framebuffer.Address, length);
        }
        else
        {
            // Defensive fallback (never observed on this app's target platform, but a raw-pixel
            // copy across two independently-allocated buffers shouldn't silently corrupt the image
            // if that assumption ever stops holding) - copy row by row against each buffer's own
            // real stride.
            int rowBytesToCopy = Math.Min(sourceRowBytes, destRowBytes);
            var rowBuffer = new byte[rowBytesToCopy];
            for (int y = 0; y < source.Height; y++)
            {
                IntPtr srcRow = IntPtr.Add(sourcePixels, y * sourceRowBytes);
                IntPtr dstRow = IntPtr.Add(framebuffer.Address, y * destRowBytes);
                Marshal.Copy(srcRow, rowBuffer, 0, rowBytesToCopy);
                Marshal.Copy(rowBuffer, 0, dstRow, rowBytesToCopy);
            }
        }

        return writeable;
    }
}
