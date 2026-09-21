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
/// The <see cref="LruCache{TKey,TValue}"/> is now internally locked, so <see cref="Get"/> /
/// <see cref="TryGetCached"/> are safe to call from any thread (the startup Home load builds its
/// cover-wall off the UI thread). <see cref="DecodeFromDisk"/> still touches no cache state at all.
/// </summary>
public static class CoverImageCache
{
    private const int MaxEntries = 5000;

    private static readonly LruCache<string, Bitmap> _cache = new(MaxEntries);

    /// <summary>A cache hit, or decode-then-store if a file exists (custom cover preferred). Any thread.</summary>
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

    /// <summary>In-memory lookup only - never touches the disk. Any thread.</summary>
    public static bool TryGetCached(string idKey, out Bitmap? bitmap) => _cache.TryGetValue(idKey, out bitmap);

    /// <summary>
    /// Decodes the on-disk cover for <paramref name="idKey"/> with <b>no</b> cache interaction -
    /// safe to call off the UI thread. A user-picked cover in <see cref="CustomCoverPaths"/> wins
    /// over the generated one. Returns null for a missing or unreadable file.
    /// </summary>
    public static Bitmap? DecodeFromDisk(string idKey)
    {
        if (CoverPipelineStats.SimulatedDecodeDelayMs > 0)
        {
            System.Threading.Thread.Sleep(CoverPipelineStats.SimulatedDecodeDelayMs);
        }

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

    /// <summary>Path of the cover file to decode for <paramref name="idKey"/> (a user-picked custom cover wins over the generated one), or empty when there is none. Shared with <see cref="GridCoverDecoder"/>.</summary>
    internal static string ResolveCoverFile(string idKey) => ResolveFile(idKey);

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
        if (File.Exists(generated))
        {
            return generated;
        }

        // A remote library's issue has no file to generate a thumbnail from; its cover is downloaded into its
        // own directory (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md section 7.4).
        if (int.TryParse(idKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out int remoteId))
        {
            string peer = Sharing.PeerCoverPaths.GetCachePath(remoteId);
            if (File.Exists(peer))
            {
                return peer;
            }
        }

        return string.Empty;
    }

    /// <summary>Adds <paramref name="decoded"/> under <paramref name="idKey"/> unless another decode
    /// already populated it. Any thread - the check-then-add is benignly racy (a concurrent decode
    /// of the same key just wastes one decode; the loser's bitmap is dropped by <see cref="LruCache{TKey,TValue}.Add"/>).</summary>
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
        GridCoverCache.Shared.Remove(key);
        CoverThumbnailPaths.DeleteCachedThumbnail(issueId);
        CustomCoverPaths.Delete(issueId);
    }

    /// <summary>Drops every in-memory entry - after a library-rebuild purge, so stale bitmaps for reused ids aren't served.</summary>
    public static void Clear()
    {
        _cache.Clear();
        GridCoverCache.Shared.Clear();
    }

    /// <summary>Number of decoded full-size bitmaps held (harness/tests).</summary>
    internal static int CachedCount => _cache.Count;

    /// <summary>Drops only the in-memory entry for one key, leaving the on-disk file alone - for a
    /// caller that just wrote fresh content to that path itself (custom covers).</summary>
    public static void InvalidateMemoryOnly(string idKey)
    {
        _cache.Remove(idKey);
        GridCoverCache.Shared.Remove(idKey);
    }
}
