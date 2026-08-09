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
    private const int MaxEntries = 1000;

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
}
