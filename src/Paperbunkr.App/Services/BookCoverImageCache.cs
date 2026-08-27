using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// In-memory <see cref="Bitmap"/> cache over <see cref="BookCoverThumbnailPaths"/>' on-disk cache -
/// mirrors <see cref="CoverImageCache"/> for comics, including its bounded-size, miss-not-cached,
/// and stem-keyed rationale (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md
/// §3; docs/superpowers/specs/2026-08-27-cover-thumbnail-identity-validation-design.md).
/// UI-thread-only, same assumption as <see cref="CoverImageCache"/>.
/// </summary>
public static class BookCoverImageCache
{
    private const int MaxEntries = 5000;

    private static readonly LruCache<string, Bitmap> _cache = new(MaxEntries);

    /// <summary>Decoded cover for a <see cref="CoverFingerprint.Stem"/>, or null when no matching
    /// file exists on disk.</summary>
    public static Bitmap? Get(string stem)
    {
        if (_cache.TryGetValue(stem, out var cached))
        {
            return cached;
        }

        string path = BookCoverThumbnailPaths.GetCachePath(stem);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new Bitmap(path);
            _cache.Add(stem, bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Convenience overload - resolves the stem from the book's current file path
    /// (books have no persisted size, so the fingerprint is path-only).</summary>
    public static Bitmap? Get(int bookId, string? filePath) =>
        Get(CoverFingerprint.Stem(bookId, filePath, null));

    /// <summary>Drops every in-memory entry for <paramref name="bookId"/> (any fingerprint) and
    /// deletes its on-disk files; call whenever a <c>Book</c> row is deleted.</summary>
    public static void Invalidate(int bookId)
    {
        string prefix = $"{bookId}-";
        _cache.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        BookCoverThumbnailPaths.DeleteCachedThumbnail(bookId);
    }
}
