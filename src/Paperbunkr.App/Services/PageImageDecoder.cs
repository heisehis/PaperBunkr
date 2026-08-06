using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using GdiBitmap = System.Drawing.Bitmap;

namespace Paperbunkr.App.Services;

/// <summary>
/// Built on the confirmed-working engine path (docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md
/// §2): <c>Providers.Readers.CreateSourceProvider(path)</c> (the same call
/// <c>ComicBook.CreateImageProvider</c> makes internally, used directly here since nothing else
/// about a <see cref="cYo.Projects.ComicRack.Engine.ComicBook"/> is needed just to decode pages) →
/// <see cref="ImageProvider.Open"/> → <see cref="ImageProvider.GetByteImage"/>/<see cref="ImageProvider.GetImage"/>.
/// Keeps at most 3 decoded bitmaps alive (previous/current/next page) — paged mode's virtualization
/// window, per spec §5.
/// </summary>
public class PageImageDecoder : IPageImageDecoder
{
    private readonly ImageProvider _provider;
    private readonly Dictionary<int, AvaloniaBitmap> _cache = new();

    private PageImageDecoder(ImageProvider provider)
    {
        _provider = provider;
    }

    /// <summary>Opens the archive at <paramref name="filePath"/>, or returns null if it can't be opened at all (missing file, unsupported format, corrupt/empty archive).</summary>
    public static PageImageDecoder? TryOpen(string filePath)
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

        return new PageImageDecoder(provider);
    }

    public int PageCount => _provider.Count;

    public AvaloniaBitmap GetPage(int pageIndex)
    {
        if (!_cache.TryGetValue(pageIndex, out var bitmap))
        {
            bitmap = Decode(pageIndex);
            _cache[pageIndex] = bitmap;
        }

        TrimCache(pageIndex);
        return bitmap;
    }

    /// <summary>
    /// Tries the raw-bytes path first — works directly for standard JPEG/PNG pages via Avalonia's
    /// own Skia-based decoder, no System.Drawing involved. Falls back to the engine's own
    /// System.Drawing.Bitmap decode (used for exotic formats its codec providers handle specially
    /// - WebP/HEIF/JPEG2000/JPEGXL) re-encoded to PNG for Avalonia to load.
    /// </summary>
    private AvaloniaBitmap Decode(int pageIndex)
    {
        try
        {
            byte[]? bytes = _provider.GetByteImage(pageIndex);
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

        using GdiBitmap gdiBitmap = _provider.GetImage(pageIndex);
        using var pngStream = new MemoryStream();
        gdiBitmap.Save(pngStream, ImageFormat.Png);
        pngStream.Position = 0;
        return new AvaloniaBitmap(pngStream);
    }

    private void TrimCache(int currentIndex)
    {
        var keep = new HashSet<int> { currentIndex - 1, currentIndex, currentIndex + 1 };
        foreach (int staleIndex in _cache.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _cache[staleIndex].Dispose();
            _cache.Remove(staleIndex);
        }
    }

    public void Dispose()
    {
        foreach (var bitmap in _cache.Values)
        {
            bitmap.Dispose();
        }

        _cache.Clear();
        _provider.Dispose();
    }
}
