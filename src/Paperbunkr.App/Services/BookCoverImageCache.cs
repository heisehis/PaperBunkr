using System.Globalization;
using System.IO;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services.Covers;

namespace Paperbunkr.App.Services;

/// <summary>
/// In-memory <see cref="Bitmap"/> cache over <see cref="BookCoverThumbnailPaths"/>' on-disk cache -
/// mirrors <see cref="CoverImageCache"/> for comics, including its bounded-size, miss-not-cached,
/// and id-keyed rationale (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-
/// design.md). UI-thread-only, same assumption as <see cref="CoverImageCache"/>.
/// </summary>
public static class BookCoverImageCache
{
    private const int MaxEntries = 5000;

    private static readonly LruCache<string, Bitmap> _cache = new(MaxEntries);

    /// <summary>Decoded cover for an id key, or null when no file exists (custom cover preferred).</summary>
    public static Bitmap? Get(string idKey)
    {
        if (_cache.TryGetValue(idKey, out var cached))
        {
            return cached;
        }

        string path = ResolveFile(idKey);
        if (path.Length == 0)
        {
            return null;
        }

        try
        {
            var bitmap = new Bitmap(path);
            _cache.Add(idKey, bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Convenience overload - the file-path argument is ignored (see <see cref="CoverFingerprint"/>).</summary>
    public static Bitmap? Get(int bookId, string? filePath) =>
        Get(bookId.ToString(CultureInfo.InvariantCulture));

    private static string ResolveFile(string idKey)
    {
        if (int.TryParse(idKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            string custom = CustomBookCoverPaths.GetCachePath(id);
            if (File.Exists(custom))
            {
                return custom;
            }
        }

        string generated = BookCoverThumbnailPaths.GetCachePath(idKey);
        return File.Exists(generated) ? generated : string.Empty;
    }

    /// <summary>Drops the in-memory entry for <paramref name="bookId"/> and deletes its on-disk files - generated and custom.</summary>
    public static void Invalidate(int bookId)
    {
        string key = bookId.ToString(CultureInfo.InvariantCulture);
        _cache.Remove(key);
        BookCoverThumbnailPaths.DeleteCachedThumbnail(bookId);
        CustomBookCoverPaths.Delete(bookId);
    }

    /// <summary>Drops every in-memory entry - after a library-rebuild purge.</summary>
    public static void Clear() => _cache.Clear();

    /// <summary>Drops only the in-memory entry for one key, leaving the on-disk file.</summary>
    public static void InvalidateMemoryOnly(string idKey) => _cache.Remove(idKey);
}
