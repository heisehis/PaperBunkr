using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Downloads and caches an arc-linked reading list's cover art (docs/superpowers/specs/2026-08-22-
/// cbl-manager-arc-cover-and-synopsis-design.md) - <c>ReadingList.CoverImageUrl</c>
/// (fetched via <c>IReadingListSource.GetArcOverviewAsync</c> at Create/Refresh time) was being
/// saved to the database but never actually rendered anywhere - this closes that gap. Same
/// on-disk-cache-plus-bounded-in-memory-LRU shape as <see cref="CoverImageCache"/>, adapted for a
/// remote URL instead of a local file: the on-disk copy under <see cref="ArcCoverPaths"/> means a
/// list's cover only ever needs downloading once, not on every app launch.
/// </summary>
public static class ArcCoverImageCache
{
    private const int MaxEntries = 50; // generous relative to how many arc-linked lists exist in a typical library - not a large-library-scale surface like CoverImageCache

    private static readonly LruCache<int, Bitmap> _cache = new(MaxEntries);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Cache-hit-only lookup - returns null without triggering a download; call <see cref="DownloadAndCacheAsync"/> to actually fetch a missing cover.</summary>
    public static Bitmap? Get(int readingListId)
    {
        if (_cache.TryGetValue(readingListId, out var cached))
        {
            return cached;
        }

        string path = ArcCoverPaths.GetCachePath(readingListId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new Bitmap(path);
            _cache.Add(readingListId, bitmap);
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads <paramref name="url"/>, caches it to disk keyed on <paramref name="readingListId"/>,
    /// and returns the decoded <see cref="Bitmap"/> - or null on any failure. Best-effort only, same
    /// "never blocks the caller" contract as <c>IReadingListSource.GetArcOverviewAsync</c> itself:
    /// a broken/unreachable cover image URL never stops the reading list from working.
    /// </summary>
    public static async Task<Bitmap?> DownloadAndCacheAsync(int readingListId, string url, CancellationToken cancellationToken)
    {
        string path = ArcCoverPaths.GetCachePath(readingListId);
        try
        {
            byte[] bytes = await Http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);

            var bitmap = new Bitmap(path);
            _cache.Add(readingListId, bitmap);
            return bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException or TaskCanceledException)
        {
            return null;
        }
    }

    private const int ThumbnailLongestEdge = 400;
    private const int JpegQuality = 85;

    /// <summary>
    /// Sets a user-picked local image as the list's cover, overwriting whatever's currently cached
    /// at this same path - the local pick and the remote <see cref="DownloadAndCacheAsync"/> path
    /// share one physical cache slot per list, so there's no separate precedence system to
    /// maintain (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md). Mirrors
    /// <see cref="CoverThumbnailService.TrySetCustomCover"/> exactly (same resize/encode
    /// constants), adapted for a <see cref="ReadingList"/> id instead of an Issue id.
    /// </summary>
    public static bool TrySetCustomCover(int readingListId, string sourceImagePath)
    {
        string destPath = ArcCoverPaths.GetCachePath(readingListId);
        try
        {
            using var source = new Bitmap(sourceImagePath);
            var size = source.PixelSize;
            int longest = Math.Max(size.Width, size.Height);
            if (longest <= 0)
            {
                return false;
            }

            double scale = Math.Min(1.0, (double)ThumbnailLongestEdge / longest);
            var target = new PixelSize(
                Math.Max(1, (int)Math.Round(size.Width * scale)),
                Math.Max(1, (int)Math.Round(size.Height * scale)));

            using Bitmap scaled = source.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
            scaled.Save(destPath, new JpegBitmapEncoderOptions { Quality = JpegQuality });

            _cache.Add(readingListId, new Bitmap(destPath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Drops readingListId's in-memory entry and on-disk file - call when the list itself is deleted, same rationale as <see cref="CoverImageCache.Invalidate"/>.</summary>
    public static void Invalidate(int readingListId)
    {
        _cache.Remove(readingListId);
        try
        {
            string path = ArcCoverPaths.GetCachePath(readingListId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
