using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// In-memory-only URL-keyed cache for browsing an external provider's cover candidates (MangaBaka's
/// multi-cover archive, docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-design.md
/// §2) - same HTTP-GET-and-decode approach as <see cref="ArcCoverImageCache.DownloadAndCacheAsync"/>,
/// but never writes to disk: these are transient browse thumbnails, not a persisted cover. Only the
/// one the user actually picks gets written, via <see cref="CoverThumbnailService.TrySetCustomCoverFromBytes"/>.
/// </summary>
public static class ProviderCoverCandidateCache
{
    private const int MaxEntries = 100; // one picker session's worth of candidates across a couple of series, generous but bounded

    private static readonly LruCache<string, Bitmap> _cache = new(MaxEntries);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Downloads and decodes <paramref name="url"/> - or returns null on any failure,
    /// matching <see cref="ArcCoverImageCache"/>'s "never blocks the caller" contract.</summary>
    public static async Task<Bitmap?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        try
        {
            byte[] bytes = await Http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
            using var stream = new System.IO.MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            _cache.Add(url, bitmap);
            return bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or NotSupportedException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Downloads the full-resolution bytes for the picked candidate - a separate call from
    /// <see cref="FetchAsync"/> since the grid only ever needs the (usually smaller) thumbnail
    /// variant decoded, while applying a cover needs the raw bytes for
    /// <see cref="CoverThumbnailService.TrySetCustomCoverFromBytes"/>, not a decoded <see cref="Bitmap"/>.</summary>
    public static async Task<byte[]?> DownloadBytesAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await Http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
