using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
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
    private const int ThumbnailLongestEdge = 200; // rail tiles render at 80x112 (ReaderScreen.axaml Border.thumb) — headroom for HiDPI

    private readonly ImageProvider _provider;
    private readonly Dictionary<int, AvaloniaBitmap> _cache = new();
    private readonly Dictionary<int, AvaloniaBitmap> _thumbnailCache = new();

    // GetPage runs on the UI thread (RefreshCurrentPage) while GetThumbnail runs on
    // ReaderScreenViewModel's background thumbnail-generation task, both against the same
    // ImageProvider/archive handle and the same two dictionaries - real bug found in production:
    // opening an issue calls RefreshCurrentPage() for the current page (almost always page 0)
    // synchronously right before the background loop's own first iteration decodes page 0 too,
    // racing on the same non-thread-safe archive read and leaving the first Reader thumbnail
    // blank (the exception was swallowed by StartThumbnailGeneration's catch). Not just a page-0
    // problem either - navigating pages while thumbnails are still generating races the same way.
    private readonly object _sync = new();

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
        lock (_sync)
        {
            if (!_cache.TryGetValue(pageIndex, out var bitmap))
            {
                bitmap = Decode(pageIndex);
                _cache[pageIndex] = bitmap;
            }

            TrimCache(pageIndex);
            return bitmap;
        }
    }

    /// <summary>
    /// Independent of <see cref="GetPage"/>'s 3-page full-res cache/eviction window - decodes via
    /// the same <see cref="Decode"/> path but immediately downsizes, and caches every thumbnail
    /// for the currently open issue (cheap at thumbnail size, unlike full-res pages).
    /// </summary>
    public AvaloniaBitmap GetThumbnail(int pageIndex)
    {
        lock (_sync)
        {
            if (_thumbnailCache.TryGetValue(pageIndex, out var cached))
            {
                return cached;
            }

            using AvaloniaBitmap full = Decode(pageIndex);
            var size = full.PixelSize;
            int longest = Math.Max(size.Width, size.Height);
            double scale = longest > 0 ? Math.Min(1.0, (double)ThumbnailLongestEdge / longest) : 1.0;
            var target = new PixelSize(
                Math.Max(1, (int)Math.Round(size.Width * scale)),
                Math.Max(1, (int)Math.Round(size.Height * scale)));

            var thumbnail = full.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
            _thumbnailCache[pageIndex] = thumbnail;
            return thumbnail;
        }
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

        foreach (var bitmap in _thumbnailCache.Values)
        {
            bitmap.Dispose();
        }

        _thumbnailCache.Clear();

        _provider.Dispose();
    }
}
