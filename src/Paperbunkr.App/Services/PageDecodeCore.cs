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
            if (bytes is { Length: > 0 })
            {
                using var byteStream = new MemoryStream(bytes);
                return new AvaloniaBitmap(byteStream);
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
}
