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
/// UI-thread-only - every current caller (<c>SeriesCardSample.FromSeries</c>,
/// <c>DetailTabsViewModel.LoadSeries</c>, <c>DetailScreenViewModel.LoadSeries</c>) runs
/// synchronously on the UI thread, so this isn't locked (matches <see cref="LruCache{TKey,TValue}"/>'s own assumption).
/// </summary>
public static class CoverImageCache
{
    private const int MaxEntries = 5000;

    private static readonly LruCache<int, Bitmap> _cache = new(MaxEntries);

    public static Bitmap? Get(int issueId)
    {
        if (_cache.TryGetValue(issueId, out var cached))
        {
            return cached;
        }

        string path = CoverThumbnailPaths.GetCachePath(issueId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new Bitmap(path);
            _cache.Add(issueId, bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
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
}
