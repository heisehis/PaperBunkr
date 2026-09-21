using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// Small thumbnails for covers that only exist as a remote URL (the Wanted screen and a series' Missing Issues: issues the library doesn't
/// own have no local file). The original bytes are kept on disk under a hash of the URL, so a cover downloads once and survives restarts;
/// what the UI gets is a display-size bitmap decoded off the UI thread and held in a bounded in-memory LRU.
/// Downloads are throttled so a page of several hundred cards can't open several hundred connections at once. Best-effort: any failure is a
/// null cover (the row's placeholder stays), never an exception.
/// </summary>
public static class RemoteCoverCache
{
    /// <summary>Wide enough for the largest place these are shown (the scraper review dialogs' 140 px preview at 1.5x).</summary>
    public const int DecodeWidth = 240;
    private const int MaxMemoryEntries = 300;
    private const int MaxConcurrentDownloads = 4;

    private static readonly LruCache<string, Bitmap> Memory = new(MaxMemoryEntries);
    private static readonly SemaphoreSlim DownloadSlots = new(MaxConcurrentDownloads);

    /// <summary>Mutable so tests can redirect to a temp folder.</summary>
    public static string CacheDirectory { get; set; } = Paperbunkr.Data.AppDataPaths.Combine("remote-covers");

    /// <summary>Mutable so tests can serve canned bytes without a network.</summary>
    public static HttpClient Http { get; set; } = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static bool IsFetchable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>In-memory hit only; never touches disk or network, so it is safe to call from a binding.</summary>
    public static Bitmap? TryGetCached(string url) => Memory.TryGetValue(url, out var bitmap) ? bitmap : null;

    /// <summary>Memory, then disk, then network. Runs its I/O and decoding on the calling thread's continuation, so call it from a worker thread.</summary>
    public static async Task<Bitmap?> GetAsync(string url, CancellationToken cancellationToken)
    {
        if (!IsFetchable(url))
        {
            return null;
        }

        if (Memory.TryGetValue(url, out var cached))
        {
            return cached;
        }

        try
        {
            string path = PathFor(url);
            byte[]? bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : null;

            if (bytes is null)
            {
                await DownloadSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    bytes = await Http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    DownloadSlots.Release();
                }

                Directory.CreateDirectory(CacheDirectory);
                // Write to a temp name then move, so a half-written file is never mistaken for a cached cover.
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temp, path, overwrite: true);
            }

            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = Bitmap.DecodeToWidth(stream, DecodeWidth, BitmapInterpolationMode.HighQuality);
            Memory.Add(url, bitmap);
            return bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException or TaskCanceledException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string PathFor(string url) =>
        Path.Combine(CacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32] + ".img");
}
