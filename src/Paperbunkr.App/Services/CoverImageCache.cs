using System.IO;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// In-memory <see cref="Bitmap"/> cache over the on-disk cover thumbnail cache
/// (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md §3) - avoids re-decoding the same
/// JPEG from disk every time a card re-renders. Bounded via <see cref="LruCache{TKey,TValue}"/> -
/// previously cached every decoded cover for the app's entire lifetime with no eviction at all,
/// on the assumption of "a few thousand ~400px thumbnails at most". Real usage against a real
/// library disproved that: browsing Library + several Detail screens + Generate Covers grew the
/// process from ~220MB at launch to ~1.4GB within a few minutes. 1000 entries is generous headroom
/// over a large library's Library-grid working set (371 series observed in that same session) -
/// bounds total session growth without evicting mid-browse for typical libraries. Eviction only
/// drops this cache's own reference - it does not dispose the evicted Bitmap, since a still-live
/// Image control elsewhere in the app may still be bound to it (see LruCache's doc comment for the
/// real crash that caused this).
///
/// **1000 was wrong.** <see cref="LruCache{TKey,TValue}"/> disposes an entry's <c>Bitmap</c> the
/// moment it's evicted (that's the whole point - see its own doc comment). <c>LibraryScreenViewModel.LoadFromDatabase</c>
/// requests a cover for every series in one synchronous pass, before Avalonia ever measures/renders
/// any of the resulting cards - so once a single library exceeds capacity mid-pass, the *earliest*
/// cards in that same pass get their <c>Bitmap</c> disposed out from under them before the layout
/// pass that reads them ever runs, crashing with <c>ObjectDisposedException</c> on
/// <c>Ref&lt;IBitmapImpl&gt;</c> from deep inside <c>Image.MeasureOverride</c>. Found for real
/// against a 1992-series library (this cache's capacity was sized against a 371-series library,
/// per the paragraph above, which is why it never fired before). Bumped to comfortably clear that
/// with the same kind of margin the original 1000 was meant to give 371.
///
/// Misses are deliberately NOT cached: a series looked up before "Generate Covers" runs would
/// otherwise permanently remember "no thumbnail" even after the file appears on disk. A cheap
/// <see cref="File.Exists"/> re-check on the next lookup is enough to self-heal once a screen
/// reloads its cards - no separate cache-invalidation mechanism needed.
///
/// The <see cref="LruCache{TKey,TValue}"/> itself is UI-thread-only (see its own doc comment).
/// <see cref="DecodeFromDisk"/> is the one deliberate exception - it touches no cache state at
/// all, so <see cref="AsyncCoverImage"/> can call it from a threadpool thread to keep JPEG decode
/// off the UI thread during Library scrolling. Everything that reads or mutates <see cref="_cache"/>
/// (<see cref="Get"/>, <see cref="TryGetCached"/>, <see cref="StoreIfAbsent"/>,
/// <see cref="Invalidate"/>) still runs on the UI thread only.
/// </summary>
public static class CoverImageCache
{
    private const int MaxEntries = 5000;

    private static readonly LruCache<int, Bitmap> _cache = new(MaxEntries);

    /// <summary>
    /// Synchronous convenience path - a cache hit, or decode-then-store if the file exists. Still
    /// used by the Detail/Home/Events/Smart view models that resolve a handful of covers eagerly
    /// while already on the UI thread; the virtualized Library grids go through
    /// <see cref="AsyncCoverImage"/> instead so their decode never blocks layout.
    /// </summary>
    public static Bitmap? Get(int issueId)
    {
        if (_cache.TryGetValue(issueId, out var cached))
        {
            return cached;
        }

        var decoded = DecodeFromDisk(issueId);
        return decoded is null ? null : StoreIfAbsent(issueId, decoded);
    }

    /// <summary>In-memory lookup only - never touches the disk. UI-thread only.</summary>
    public static bool TryGetCached(int issueId, out Bitmap? bitmap) => _cache.TryGetValue(issueId, out bitmap);

    /// <summary>
    /// Decodes <paramref name="issueId"/>'s on-disk thumbnail with <b>no</b> cache interaction -
    /// the one method here safe to call off the UI thread (<see cref="_cache"/> is not). Returns
    /// null for a missing or unreadable file. The caller is responsible for handing the result to
    /// <see cref="StoreIfAbsent"/> on the UI thread if it wants it cached.
    /// </summary>
    public static Bitmap? DecodeFromDisk(int issueId)
    {
        string path = CoverThumbnailPaths.GetCachePath(issueId);
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
    /// Adds <paramref name="decoded"/> under <paramref name="issueId"/> unless another decode
    /// already populated that key (two recycled containers can race a decode for the same cover) -
    /// in which case the pre-existing instance is returned and the caller should drop
    /// <paramref name="decoded"/> so only one <c>Bitmap</c> per id is ever handed around. UI-thread only.
    /// </summary>
    public static Bitmap StoreIfAbsent(int issueId, Bitmap decoded)
    {
        if (_cache.TryGetValue(issueId, out var existing))
        {
            return existing!;
        }

        _cache.Add(issueId, decoded);
        return decoded;
    }

    /// <summary>Drops issueId's in-memory entry (if any) and deletes its on-disk file (docs bug
    /// note: <see cref="CoverThumbnailPaths.DeleteCachedThumbnail"/>) - call this whenever an
    /// <c>Issue</c> row is actually deleted, so nothing keeps serving a stale cover for a numeric
    /// id a future migration might reuse.</summary>
    public static void Invalidate(int issueId)
    {
        _cache.Remove(issueId);
        CoverThumbnailPaths.DeleteCachedThumbnail(issueId);
    }

    /// <summary>
    /// Drops only the in-memory entry, leaving the on-disk file alone - for a caller that just
    /// wrote fresh content to that exact cache path itself (docs/superpowers/specs/2026-08-23-
    /// cover-art-override-design.md's custom-cover feature) and needs the next <see cref="Get"/> to
    /// re-read it, unlike <see cref="Invalidate"/> which would delete the file it just wrote.
    /// </summary>
    public static void InvalidateMemoryOnly(int issueId) => _cache.Remove(issueId);
}
