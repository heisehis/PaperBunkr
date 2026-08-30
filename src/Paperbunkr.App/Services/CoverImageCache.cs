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
/// The <see cref="LruCache{TKey,TValue}"/> itself is UI-thread-only (see its own doc comment).
/// <see cref="DecodeFromDisk"/> is the one deliberate exception - it touches no cache state at
/// all, so <see cref="AsyncCoverImage"/> can call it from a threadpool thread to keep JPEG decode
/// off the UI thread during Library scrolling. Everything that reads or mutates <see cref="_cache"/>
/// (<see cref="Get(string)"/>, <see cref="TryGetCached"/>, <see cref="StoreIfAbsent"/>,
/// <see cref="Invalidate"/>) still runs on the UI thread only.
/// </summary>
public static class CoverImageCache
{
    private const int MaxEntries = 5000;

    private static readonly LruCache<string, Bitmap> _cache = new(MaxEntries);

    /// <summary>
    /// Synchronous convenience path - a cache hit, or decode-then-store if the file exists. Still
    /// used by the Detail/Home/Events/Smart view models that resolve a handful of covers eagerly
    /// while already on the UI thread; the virtualized Library grids go through
    /// <see cref="AsyncCoverImage"/> instead so their decode never blocks layout.
    /// </summary>
    public static Bitmap? Get(string stem)
    {
        if (_cache.TryGetValue(stem, out var cached))
        {
            return cached;
        }

        var decoded = DecodeFromDisk(stem);
        return decoded is null ? null : StoreIfAbsent(stem, decoded);
    }

    /// <summary>Convenience overload - resolves the stem from the issue's current file identity.</summary>
    public static Bitmap? Get(int issueId, string? filePath, long? fileSize) =>
        Get(CoverFingerprint.Stem(issueId, filePath, fileSize));

    /// <summary>In-memory lookup only - never touches the disk. UI-thread only.</summary>
    public static bool TryGetCached(string stem, out Bitmap? bitmap) => _cache.TryGetValue(stem, out bitmap);

    /// <summary>
    /// Decodes <paramref name="stem"/>'s on-disk thumbnail with <b>no</b> cache interaction -
    /// the one method here safe to call off the UI thread (<see cref="_cache"/> is not). Returns
    /// null for a missing or unreadable file. The caller is responsible for handing the result to
    /// <see cref="StoreIfAbsent"/> on the UI thread if it wants it cached.
    /// </summary>
    public static Bitmap? DecodeFromDisk(string stem)
    {
        string path = CoverThumbnailPaths.GetCachePath(stem);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Adds <paramref name="decoded"/> under <paramref name="stem"/> unless another decode
    /// already populated that key (two recycled containers can race a decode for the same cover) -
    /// in which case the pre-existing instance is returned and the caller should drop
    /// <paramref name="decoded"/> so only one <c>Bitmap</c> per stem is ever handed around. UI-thread only.
    /// </summary>
    public static Bitmap StoreIfAbsent(string stem, Bitmap decoded)
    {
        if (_cache.TryGetValue(stem, out var existing))
        {
            return existing!;
        }

        _cache.Add(stem, decoded);
        return decoded;
    }

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
