using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// In-memory <see cref="Bitmap"/> cache over the on-disk cover thumbnail cache
/// (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md §3) - avoids re-decoding the same
/// JPEG from disk every time a card re-renders. Small enough (a few thousand ~400px thumbnails at
/// most) to just stay resident for the app's lifetime, no eviction.
///
/// Misses are deliberately NOT cached: a series looked up before "Generate Covers" runs would
/// otherwise permanently remember "no thumbnail" even after the file appears on disk. A cheap
/// <see cref="File.Exists"/> re-check on the next lookup is enough to self-heal once a screen
/// reloads its cards - no separate cache-invalidation mechanism needed.
///
/// UI-thread-only - every current caller (<c>SeriesCardSample.FromSeries</c>,
/// <c>DetailTabsViewModel.LoadSeries</c>, <c>DetailScreenViewModel.LoadSeries</c>) runs
/// synchronously on the UI thread, so this dictionary isn't locked.
/// </summary>
public static class CoverImageCache
{
    private static readonly Dictionary<int, Bitmap> _cache = new();

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
            _cache[issueId] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
