using System.IO;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// In-memory <see cref="Bitmap"/> cache over the on-disk cover thumbnail cache
/// (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md §3) - avoids re-decoding the same
/// JPEG from disk every time a card re-renders. Bounded via <see cref="LruCache{TKey,TValue}"/>.
/// Eviction only drops this cache's own reference - it does not dispose the evicted Bitmap, since
/// a still-live Image control elsewhere in the app may still be bound to it (see LruCache's doc
/// comment for the real crash that caused this).
///
/// Keyed by the <see cref="CoverFingerprint.Stem"/> string, not the bare issue id
/// (docs/superpowers/specs/2026-08-27-cover-thumbnail-identity-validation-design.md): after a
/// library rebuild that reassigned ids, an id-keyed cache would keep handing back the previous
/// entity's cover. The stem folds the issue's current file identity in, so a stale file simply
/// isn't found and the card falls back to its <c>CoverBrush</c> until <c>GenerateAllAsync</c>
/// regenerates it.
///
/// Misses are deliberately NOT cached: a stem looked up before "Generate Covers" runs would
/// otherwise permanently remember "no thumbnail" even after the file appears on disk. A cheap
/// <see cref="File.Exists"/> re-check on the next lookup is enough to self-heal once a screen
/// reloads its cards.
///
/// UI-thread-only - every current caller runs synchronously on the UI thread, so this isn't
/// locked (matches <see cref="LruCache{TKey,TValue}"/>'s own assumption).
/// </summary>
public static class CoverImageCache
{
    private const int MaxEntries = 5000;

    private static readonly LruCache<string, Bitmap> _cache = new(MaxEntries);

    /// <summary>Decoded cover for a <see cref="CoverFingerprint.Stem"/>, or null when no matching
    /// file exists on disk (the caller shows its fallback brush).</summary>
    public static Bitmap? Get(string stem)
    {
        if (_cache.TryGetValue(stem, out var cached))
        {
            return cached;
        }

        string path = CoverThumbnailPaths.GetCachePath(stem);
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

    /// <summary>Convenience overload - resolves the stem from the issue's current file identity.</summary>
    public static Bitmap? Get(int issueId, string? filePath, long? fileSize) =>
        Get(CoverFingerprint.Stem(issueId, filePath, fileSize));

    /// <summary>Drops every in-memory entry for <paramref name="issueId"/> (any fingerprint) and
    /// deletes its on-disk files - call this whenever an <c>Issue</c> row is actually deleted, so
    /// nothing keeps serving a stale cover for a numeric id a future migration might reuse.</summary>
    public static void Invalidate(int issueId)
    {
        string prefix = $"{issueId}-";
        _cache.RemoveWhere(key => key.StartsWith(prefix, System.StringComparison.Ordinal));
        CoverThumbnailPaths.DeleteCachedThumbnail(issueId);
    }

    /// <summary>
    /// Drops only the in-memory entry for one stem, leaving the on-disk file alone - for a caller
    /// that just wrote fresh content to that exact cache path itself (docs/superpowers/specs/
    /// 2026-08-23-cover-art-override-design.md's custom-cover feature) and needs the next
    /// <see cref="Get(string)"/> to re-read it, unlike <see cref="Invalidate"/> which would delete
    /// the file it just wrote.
    /// </summary>
    public static void InvalidateMemoryOnly(string stem) => _cache.Remove(stem);
}
