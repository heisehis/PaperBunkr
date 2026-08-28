using System.IO;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// In-memory <see cref="Bitmap"/> cache over <see cref="BookCoverThumbnailPaths"/>' on-disk cache -
/// mirrors <see cref="CoverImageCache"/> for comics, including its bounded-size and
/// miss-not-cached rationale (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md
/// §3). UI-thread-only, same assumption as <see cref="CoverImageCache"/>.
///
/// Capacity matches <see cref="CoverImageCache"/>'s (also bumped from an original 1000) rather
/// than a separately-tuned number - same eviction-disposes-a-still-bound-Bitmap hazard applies
/// here (<c>BooksScreenViewModel.LoadFromDatabase</c> requests a cover for every book in one
/// synchronous pass, same as Library's), confirmed for real against a 1992-series comic library
/// even though no Book library anywhere near that size has been tested yet - no reason to ship
/// the same known failure mode into new code just because it hasn't been hit here yet.
/// </summary>
public static class BookCoverImageCache
{
    private const int MaxEntries = 5000;

    private static readonly LruCache<int, Bitmap> _cache = new(MaxEntries);

    public static Bitmap? Get(int bookId)
    {
        if (_cache.TryGetValue(bookId, out var cached))
        {
            return cached;
        }

        string path = BookCoverThumbnailPaths.GetCachePath(bookId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new Bitmap(path);
            _cache.Add(bookId, bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Drops bookId's in-memory entry (if any) and deletes its on-disk file - mirrors
    /// <see cref="CoverImageCache.Invalidate"/>; call whenever a <c>Book</c> row is deleted.</summary>
    public static void Invalidate(int bookId)
    {
        _cache.Remove(bookId);
        BookCoverThumbnailPaths.DeleteCachedThumbnail(bookId);
    }

    /// <summary>Drops only bookId's in-memory entry, leaving the on-disk file - mirrors
    /// <see cref="CoverImageCache.InvalidateMemoryOnly"/>. Used right after
    /// <see cref="BookCoverThumbnailService.TrySetCustomCover"/> has just written a new file, so
    /// the next <see cref="Get"/> re-reads it without <see cref="Invalidate"/> deleting it first.</summary>
    public static void InvalidateMemoryOnly(int bookId) => _cache.Remove(bookId);
}
