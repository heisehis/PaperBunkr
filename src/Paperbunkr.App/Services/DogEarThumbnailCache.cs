using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// In-memory cache of a comic's second-page thumbnail, decoded on demand for the
/// <c>DogEarThumbnails</c> hover/selected peek (docs/superpowers/specs/2026-09-13-preferences-
/// cosmetic-toggles-design.md). Mirrors <see cref="CoverImageCache"/>'s bounded-<see cref="LruCache{TKey,TValue}"/>
/// shape, but there is no on-disk pregenerated file to fall back to the way page-0 covers have
/// (<c>CoverThumbnailService</c>) - a miss always decodes fresh via <see cref="PageDecodeCore"/>,
/// which opens and closes the archive itself rather than standing up a full
/// <see cref="Reader.ReaderImagePipeline"/> reader session for one page.
///
/// <para>
/// The page is decoded at <see cref="DecodeWidth"/>, not whole (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md,
/// measured 2026-10-07): the peek is drawn 34 px wide, but this cache used to keep the full page, 23 MB for an ordinary
/// 1988x3056 page and up to 85 MB for a large scan, one per comic hovered. A heap dump of a normal session found 28 of them
/// holding 634 MB.
/// </para>
/// </summary>
public static class DogEarThumbnailCache
{
    private const int MaxEntries = 200;

    /// <summary>Decode width in pixels: the 34 px peek at up to about 2.8x display scaling. 200 of these are roughly 11 MB.</summary>
    public const int DecodeWidth = 96;

    private static readonly LruCache<string, Bitmap> Cache = new(MaxEntries);

    /// <summary>In-memory lookup only - never touches disk. Safe to call from any thread.</summary>
    public static Bitmap? TryGetCached(string stem) => Cache.TryGetValue(stem, out var cached) ? cached : null;

    /// <summary>A cache hit, or decode-then-store on a miss. <paramref name="filePath"/> is the
    /// issue's own file; returns null if the file can't be opened or has no second page. Intended
    /// for a background thread - decoding is not cheap enough to call on the UI thread.</summary>
    public static Bitmap? Get(string stem, string filePath)
    {
        if (Cache.TryGetValue(stem, out var cached))
        {
            return cached;
        }

        var decoded = PageDecodeCore.DecodeSinglePageToWidth(filePath, pageIndex: 1, DecodeWidth);
        if (decoded is null)
        {
            return null;
        }

        Cache.Add(stem, decoded);
        return decoded;
    }
}
