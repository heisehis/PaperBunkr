using System.Drawing.Imaging;
using System.IO;
using Avalonia.Media.Imaging;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using GdiBitmap = System.Drawing.Bitmap;

namespace Paperbunkr.App.Services;

/// <summary>
/// The archive-open and raw-page-decode logic shared by <see cref="PageImageDecoder"/> (paged
/// mode's existing ±1-window decoder) and <see cref="PageDecodeService"/> (the two-tier/virtualized
/// decoder added for continuous scroll, docs/superpowers/specs/2026-08-10-reader-polish-continuous-
/// scroll-chrome-overlays-design.md §3) - extracted rather than duplicated, so both decoders stay
/// byte-for-byte consistent on how a page is actually read off disk.
/// </summary>
internal static class PageDecodeCore
{
    /// <summary>
    /// One-shot: decode page <paramref name="pageIndex"/> of the file at <paramref name="filePath"/>
    /// and dispose the provider. For callers that need a single page and nothing else - cover /
    /// thumbnail generation, plugin page fetch - without standing up the full
    /// <see cref="Reader.ReaderImagePipeline"/> (which spins a background consumer thread per
    /// instance). Returns <see langword="null"/> if the file can't be opened or the page decoded.
    /// </summary>
    public static AvaloniaBitmap? DecodeSinglePage(string filePath, int pageIndex = 0)
    {
        var provider = TryOpenProvider(filePath);
        if (provider is null || pageIndex < 0 || pageIndex >= provider.Count)
        {
            provider?.Dispose();
            return null;
        }

        try
        {
            return Decode(provider, pageIndex);
        }
        catch
        {
            return null;
        }
        finally
        {
            provider.Dispose();
        }
    }

    /// <summary>
    /// One-shot like <see cref="DecodeSinglePage"/>, but the page comes back <paramref name="width"/> pixels wide (aspect kept).
    /// For a caller that shows a page small and keeps it: a full comic page is 20-85 MB decoded, a 96 px one is
    /// under 100 KB. Standard JPEG/PNG pages are scaled while decoding, so the full page never exists in memory; the exotic
    /// formats that need the engine's own decoder are decoded whole, scaled, and the whole page freed before returning.
    /// </summary>
    public static AvaloniaBitmap? DecodeSinglePageToWidth(string filePath, int pageIndex, int width)
    {
        var provider = TryOpenProvider(filePath);
        if (provider is null || pageIndex < 0 || pageIndex >= provider.Count)
        {
            provider?.Dispose();
            return null;
        }

        try
        {
            try
            {
                if (provider.GetByteImage(pageIndex) is { Length: > 0 } bytes)
                {
                    using var stream = new MemoryStream(bytes, writable: false);
                    return AvaloniaBitmap.DecodeToWidth(stream, width, Avalonia.Media.Imaging.BitmapInterpolationMode.MediumQuality);
                }
            }
            catch
            {
                // Not a format Avalonia decodes directly - fall through to the whole-page path.
            }

            using var full = Decode(provider, pageIndex);
            if (full.PixelSize.Width <= width)
            {
                return full.CreateScaledBitmap(full.PixelSize);
            }

            int height = System.Math.Max(1, (int)System.Math.Round(full.PixelSize.Height * (width / (double)full.PixelSize.Width)));
            return full.CreateScaledBitmap(new Avalonia.PixelSize(width, height), Avalonia.Media.Imaging.BitmapInterpolationMode.MediumQuality);
        }
        catch
        {
            return null;
        }
        finally
        {
            provider.Dispose();
        }
    }

    /// <summary>Opens the archive at <paramref name="filePath"/>, or returns null if it can't be opened at all (missing file, unsupported format, corrupt/empty archive).</summary>
    public static ImageProvider? TryOpenProvider(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        // CreateSourceProvider dispatches purely by file extension (FileFormat.Supports) - it
        // doesn't itself check the file exists or is readable, hence the explicit check above.
        var provider = Providers.Readers.CreateSourceProvider(filePath);
        if (provider is null)
        {
            return null;
        }

        try
        {
            provider.Open(async: false);
        }
        catch
        {
            provider.Dispose();
            return null;
        }

        if (provider.Count == 0)
        {
            provider.Dispose();
            return null;
        }

        return provider;
    }

    /// <summary>
    /// Tries the raw-bytes path first — works directly for standard JPEG/PNG pages via Avalonia's
    /// own Skia-based decoder, no System.Drawing involved. Falls back to the engine's own
    /// System.Drawing.Bitmap decode (used for exotic formats its codec providers handle specially
    /// - WebP/HEIF/JPEG2000/JPEGXL) re-encoded to PNG for Avalonia to load.
    /// </summary>
    public static AvaloniaBitmap Decode(ImageProvider provider, int pageIndex)
    {
        try
        {
            byte[]? bytes = provider.GetByteImage(pageIndex);
            var direct = TryDecodeBytes(bytes);
            if (direct is not null)
            {
                return direct;
            }
        }
        catch
        {
            // Not a standard format Avalonia can decode directly - fall through.
        }

        using GdiBitmap gdiBitmap = provider.GetImage(pageIndex);
        using var pngStream = new MemoryStream();
        gdiBitmap.Save(pngStream, ImageFormat.Png);
        pngStream.Position = 0;
        return new AvaloniaBitmap(pngStream);
    }

    /// <summary>
    /// Decode already-in-hand encoded bytes (JPEG/PNG/…) straight through Avalonia's Skia decoder,
    /// or <see langword="null"/> if they aren't a format it reads directly (caller then falls back
    /// to <see cref="Decode(ImageProvider,int)"/>'s GDI path). The reader pipeline's fast path -
    /// bytes come from a held-open <see cref="cYo.Projects.ComicRack.Engine.IO.Provider.Readers.IComicAccessorSession"/>,
    /// not a per-page archive reopen (docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md §4).
    /// </summary>
    public static AvaloniaBitmap? TryDecodeBytes(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new AvaloniaBitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
