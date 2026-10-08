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
    /// An <see cref="SKImage"/> over <paramref name="source"/>'s <b>own</b> pixels: no copy. For drawing a page through a leased
    /// canvas (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md, measured 2026-10-08): <see cref="ToSkImage"/>
    /// makes three full-page buffers to produce one image (a 24 MB byte array on the large object heap, a scratch
    /// <see cref="WriteableBitmap"/> and the image's own copy), and the reader kept one such image per visible page for as long
    /// as an image adjustment was on - four of them, over 100 MB, in the measured session.
    ///
    /// The returned image is the caller's to dispose, exactly like <see cref="ToSkImage"/>'s. It holds its own reference to the
    /// bitmap's pixel storage and releases it when the image is destroyed, so it stays valid even if the reader pipeline
    /// disposes <paramref name="source"/> first (the reason callers convert up front at all).
    ///
    /// Avalonia has no public way to reach a bitmap's Skia image (see the class remarks), so this reads two non-public members
    /// (<c>Bitmap.PlatformImpl</c> and <c>Avalonia.Skia.ImmutableBitmap._image</c>, as of Avalonia 12.1). If either is missing in
    /// a later version, or the bitmap is not a plain decoded one (a <see cref="WriteableBitmap"/>), it falls back to
    /// <see cref="ToSkImage"/>: slower and larger, never wrong. Pixels keep the bitmap's own colour and alpha type, so use it
    /// for drawing, not for code that reads raw bytes in a fixed channel order.
    /// </summary>
    public static SKImage ShareSkImage(Bitmap source) => TryShareSkImage(source) ?? ToSkImage(source);

    private static readonly System.Reflection.PropertyInfo? s_platformImpl = typeof(Bitmap).GetProperty(
        "PlatformImpl", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.FieldInfo?> s_imageFields = new();

    /// <summary>The zero-copy image, or null when this bitmap (or this Avalonia version) does not allow it. Internal for tests.</summary>
    internal static SKImage? TryShareSkImage(Bitmap source)
    {
        IDisposable? keepAlive = null;
        try
        {
            if (s_platformImpl?.GetValue(source) is not { } reference)
            {
                return null;
            }

            var referenceType = reference.GetType();
            object? impl = referenceType.GetProperty("Item")?.GetValue(reference);
            if (impl is null || impl.GetType().FullName != "Avalonia.Skia.ImmutableBitmap")
            {
                return null;
            }

            var imageField = s_imageFields.GetOrAdd(impl.GetType(), static type =>
                type.GetField("_image", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
            if (imageField?.GetValue(impl) is not SKImage inner)
            {
                return null;
            }

            // Our own counted reference to the bitmap's storage: Avalonia frees the pixels when the last one goes.
            keepAlive = referenceType.GetMethod("Clone", Type.EmptyTypes)?.Invoke(reference, null) as IDisposable;
            if (keepAlive is null)
            {
                return null;
            }

            using var pixmap = inner.PeekPixels();
            if (pixmap is null || pixmap.GetPixels() == IntPtr.Zero)
            {
                return null; // not a raster image: nothing to share
            }

            var held = keepAlive;
            var shared = SKImage.FromPixels(pixmap, (_, _) => held.Dispose());
            if (shared is not null)
            {
                keepAlive = null; // now released by the image itself
            }

            return shared;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // a changed internal shape, or a bitmap disposed under us: the caller's copy path decides what happens next
        }
        finally
        {
            keepAlive?.Dispose();
        }
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
    /// <summary>
    /// An immutable Avalonia <see cref="Bitmap"/> holding a copy of <paramref name="source"/>'s pixels. Unlike the <see cref="WriteableBitmap"/> <see cref="FromSkBitmap"/> returns, this can be handed to
    /// <c>CreateScaledBitmap</c> (which rejects a writeable one), so it is what the reader pipeline caches.
    /// </summary>
    public static Bitmap ToImmutableBitmap(SKBitmap source)
    {
        var pixelFormat = source.ColorType switch
        {
            SKColorType.Bgra8888 => PixelFormat.Bgra8888,
            SKColorType.Rgba8888 => PixelFormat.Rgba8888,
            _ => throw new NotSupportedException($"ToImmutableBitmap: unsupported SKColorType '{source.ColorType}'."),
        };
        var alphaFormat = source.AlphaType == SKAlphaType.Opaque ? AlphaFormat.Opaque : source.AlphaType == SKAlphaType.Unpremul ? AlphaFormat.Unpremul : AlphaFormat.Premul;
        return new Bitmap(pixelFormat, alphaFormat, source.GetPixels(), new PixelSize(source.Width, source.Height), new Vector(96, 96), source.RowBytes);
    }

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
