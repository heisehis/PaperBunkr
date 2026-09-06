using System.Globalization;
using System.IO;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services.Covers;

namespace Paperbunkr.App.Services;

/// <summary>
/// In-memory <see cref="Bitmap"/> cache over the on-disk cover thumbnail cache
/// (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md §3) - avoids re-decoding the same
/// JPEG from disk every time a card re-renders. Bounded via <see cref="LruCache{TKey,TValue}"/>.
/// Eviction only drops this cache's own reference - it does not dispose the evicted Bitmap, since
/// a still-live Image control elsewhere in the app may still be bound to it.
///
/// <para>
/// Keyed by the bare id string (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-
/// durability-design.md). id-reuse after a library rebuild is handled by
/// <see cref="Covers.CoverCacheState"/>'s explicit purge (which also calls <see cref="Clear"/>),
/// not by folding a file fingerprint into every key. A user-picked cover in
/// <see cref="CustomCoverPaths"/> is served ahead of the generated one.
/// </para>
///
/// <para>
/// Misses are deliberately NOT cached: a key looked up before "Generate Covers" runs would
/// otherwise permanently remember "no thumbnail" even after the file appears. A cheap
/// <see cref="File.Exists"/> re-check on the next lookup self-heals once a screen reloads.
/// </para>
///
/// The <see cref="LruCache{TKey,TValue}"/> is UI-thread-only. <see cref="DecodeFromDisk"/> is the
/// one exception - it touches no cache state, so <see cref="AsyncCoverImage"/> can call it from a
/// threadpool thread.
/// </summary>
public static class CoverImageCache
{
    private const int MaxEntries = 5000;

    private static readonly LruCache<string, Bitmap> _cache = new(MaxEntries);

    /// <summary>A cache hit, or decode-then-store if a file exists (custom cover preferred). UI-thread only.</summary>
    public static Bitmap? Get(string idKey)
    {
        if (_cache.TryGetValue(idKey, out var cached))
        {
            return cached;
        }

        var decoded = DecodeFromDisk(idKey);
        return decoded is null ? null : StoreIfAbsent(idKey, decoded);
    }

    /// <summary>Convenience overload - the file-identity arguments are ignored (see <see cref="CoverFingerprint"/>).</summary>
    public static Bitmap? Get(int issueId, string? filePath, long? fileSize) =>
        Get(issueId.ToString(CultureInfo.InvariantCulture));

    /// <summary>In-memory lookup only - never touches the disk. UI-thread only.</summary>
    public static bool TryGetCached(string idKey, out Bitmap? bitmap) => _cache.TryGetValue(idKey, out bitmap);

    /// <summary>
    /// Decodes the on-disk cover for <paramref name="idKey"/> with <b>no</b> cache interaction -
    /// safe to call off the UI thread. A user-picked cover in <see cref="CustomCoverPaths"/> wins
    /// over the generated one. Returns null for a missing or unreadable file.
    /// </summary>
    public static Bitmap? DecodeFromDisk(string idKey)
    {
        string path = ResolveFile(idKey);
        if (path.Length == 0)
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

    private static string ResolveFile(string idKey)
    {
        if (int.TryParse(idKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            string custom = CustomCoverPaths.GetCachePath(id);
            if (File.Exists(custom))
            {
                return custom;
            }
        }

        string generated = CoverThumbnailPaths.GetCachePath(idKey);
        return File.Exists(generated) ? generated : string.Empty;
    }

    /// <summary>Adds <paramref name="decoded"/> under <paramref name="idKey"/> unless another decode already populated it. UI-thread only.</summary>
    public static Bitmap StoreIfAbsent(string idKey, Bitmap decoded)
    {
        if (_cache.TryGetValue(idKey, out var existing))
        {
            return existing!;
        }

        _cache.Add(idKey, decoded);
        return decoded;
    }

    /// <summary>Drops the in-memory entry for <paramref name="issueId"/> and deletes its on-disk
    /// files - generated and custom - for use when an <c>Issue</c> row is actually deleted.</summary>
    public static void Invalidate(int issueId)
    {
        string key = issueId.ToString(CultureInfo.InvariantCulture);
        _cache.Remove(key);
        CoverThumbnailPaths.DeleteCachedThumbnail(issueId);
        CustomCoverPaths.Delete(issueId);
    }

    /// <summary>Drops every in-memory entry - after a library-rebuild purge, so stale bitmaps for reused ids aren't served.</summary>
    public static void Clear() => _cache.Clear();

    /// <summary>Drops only the in-memory entry for one key, leaving the on-disk file alone - for a
    /// caller that just wrote fresh content to that path itself (custom covers).</summary>
    public static void InvalidateMemoryOnly(string idKey) => _cache.Remove(idKey);
}
