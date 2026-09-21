using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Paperbunkr.Data;
using Paperbunkr.Sharing.Protocol;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.App.Services.Sharing;

/// <summary>
/// The host's <see cref="ISharePageSource"/>: serves page and cover bytes out of the real archives
/// (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md §5). It reuses the reader's own
/// <see cref="PageDecodeCore"/> provider/decoder, so every format the reader opens can be shared.
/// </summary>
/// <remarks>
/// Opening a RAR/7z is expensive, so a small LRU of open providers is kept (each guarded by its own
/// lock - <see cref="ImageProvider"/> is not thread-safe) and closed after <see cref="IdleTimeout"/>.
/// Callers must already have confirmed the issue is shared (<c>IShareCatalogSource.IsIssueSharedAsync</c>):
/// this class trusts the id, and only ever resolves it to a path server-side - a client can never
/// name a path.
/// </remarks>
public sealed class ArchivePageSource : ISharePageSource, IDisposable
{
    private const int JpegQuality = 85;
    private const int MaxOpenProviders = 4;

    private sealed class Entry
    {
        public required ImageProvider Provider { get; init; }
        public readonly object Lock = new();
        public DateTime LastUsedUtc;

        /// <summary>Set (under <see cref="Lock"/>) when the archive is closed by eviction/disposal; a request that acquired the entry just before must re-acquire rather than read a disposed provider.</summary>
        public bool Closed;
    }

    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<int, string?> _effectiveCoverPath;
    private readonly Dictionary<int, Entry> _open = new();
    private readonly object _gate = new();
    private bool _disposed;

    public ArchivePageSource(Func<PaperbunkrDbContext> contextFactory, Func<int, string?>? effectiveCoverPath = null)
    {
        _contextFactory = contextFactory;
        _effectiveCoverPath = effectiveCoverPath ?? CoverThumbnailService.GetEffectiveCoverPath;
    }

    /// <summary>How long an unused archive stays open. Settable so tests can force eviction.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Number of archives currently held open - observable for the bounded-resource tests.</summary>
    public int OpenArchiveCount
    {
        get
        {
            lock (_gate)
            {
                return _open.Count;
            }
        }
    }

    public Task<PagesResponse?> GetPagesAsync(int issueId, CancellationToken cancellationToken) =>
        Task.Run(() => Use(issueId, provider =>
        {
            int count = provider.Count;
            var pages = Enumerable.Range(0, count).Select(i => new PageInfoDto(i, null, null, null)).ToList();
            return (PagesResponse?)new PagesResponse(count, pages);
        }), cancellationToken);

    public Task<PageContent?> GetPageAsync(int issueId, int pageIndex, int? maxWidth, CancellationToken cancellationToken) =>
        Task.Run(() => Use(issueId, provider =>
        {
            if (pageIndex < 0 || pageIndex >= provider.Count)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (maxWidth is null)
            {
                byte[]? bytes = provider.GetByteImage(pageIndex);
                return bytes is { Length: > 0 } ? new PageContent(new MemoryStream(bytes, writable: false), Sniff(bytes)) : null;
            }

            using Bitmap page = PageDecodeCore.Decode(provider, pageIndex);
            return EncodeJpeg(page, maxWidth.Value);
        }), cancellationToken);

    public Task<PageContent?> GetCoverAsync(int issueId, int? maxWidth, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            // Prefer the cover the host actually shows (custom-wins-over-generated); it is already a small JPEG.
            string? coverPath = _effectiveCoverPath(issueId);
            if (coverPath is not null && File.Exists(coverPath))
            {
                try
                {
                    return new PageContent(new FileStream(coverPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), "image/jpeg");
                }
                catch (IOException)
                {
                    // Raced a cache rewrite - fall through to decoding the first page.
                }
            }

            return Use(issueId, provider =>
            {
                if (provider.Count == 0)
                {
                    return null;
                }

                using Bitmap page = PageDecodeCore.Decode(provider, 0);
                return EncodeJpeg(page, maxWidth ?? 400);
            });
        }, cancellationToken);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (Entry entry in _open.Values)
            {
                CloseEntry(entry);
            }

            _open.Clear();
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> against the issue's open archive under that archive's lock.
    /// Eviction can close an entry between <see cref="Acquire"/> handing it out and the lock being
    /// taken; that shows up as <see cref="Entry.Closed"/>, and we simply acquire (re-open) again.
    /// </summary>
    private T? Use<T>(int issueId, Func<ImageProvider, T?> work) where T : class
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Entry? entry = Acquire(issueId);
            if (entry is null)
            {
                return null;
            }

            lock (entry.Lock)
            {
                if (!entry.Closed)
                {
                    return work(entry.Provider);
                }
            }
        }

        return null;
    }

    private Entry? Acquire(int issueId)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            EvictIdle();

            if (_open.TryGetValue(issueId, out Entry? existing))
            {
                existing.LastUsedUtc = DateTime.UtcNow;
                return existing;
            }
        }

        // Path resolution and the (slow) archive open happen outside the gate.
        string? path;
        using (PaperbunkrDbContext context = _contextFactory())
        {
            path = context.Issues.Where(i => i.Id == issueId).Select(i => i.FilePath).FirstOrDefault();
        }

        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        ImageProvider? provider = PageDecodeCore.TryOpenProvider(path);
        if (provider is null)
        {
            return null;
        }

        lock (_gate)
        {
            // Another request may have opened the same archive while we were - keep theirs.
            if (_disposed)
            {
                provider.Dispose();
                return null;
            }

            if (_open.TryGetValue(issueId, out Entry? raced))
            {
                provider.Dispose();
                raced.LastUsedUtc = DateTime.UtcNow;
                return raced;
            }

            while (_open.Count >= MaxOpenProviders)
            {
                int oldest = _open.OrderBy(kv => kv.Value.LastUsedUtc).First().Key;
                CloseEntry(_open[oldest]);
                _open.Remove(oldest);
            }

            var entry = new Entry { Provider = provider, LastUsedUtc = DateTime.UtcNow };
            _open[issueId] = entry;
            return entry;
        }
    }

    private void EvictIdle()
    {
        DateTime cutoff = DateTime.UtcNow - IdleTimeout;
        foreach (int id in _open.Where(kv => kv.Value.LastUsedUtc < cutoff).Select(kv => kv.Key).ToList())
        {
            CloseEntry(_open[id]);
            _open.Remove(id);
        }
    }

    private static void CloseEntry(Entry entry)
    {
        // Take the entry's own lock so an in-flight page read finishes before the archive closes.
        lock (entry.Lock)
        {
            entry.Closed = true;
            entry.Provider.Dispose();
        }
    }

    private static PageContent EncodeJpeg(Bitmap source, int maxWidth)
    {
        int width = Math.Max(1, maxWidth);
        Bitmap scaled = source;
        bool ownsScaled = false;
        if (source.PixelSize.Width > width)
        {
            int height = Math.Max(1, (int)Math.Round(source.PixelSize.Height * (width / (double)source.PixelSize.Width)));
            scaled = source.CreateScaledBitmap(new PixelSize(width, height), BitmapInterpolationMode.HighQuality);
            ownsScaled = true;
        }

        try
        {
            var stream = new MemoryStream();
            // Not Save(stream, quality): that overload encodes PNG. The options overload is the JPEG one.
            scaled.Save(stream, new JpegBitmapEncoderOptions { Quality = JpegQuality });
            stream.Position = 0;
            return new PageContent(stream, "image/jpeg");
        }
        finally
        {
            if (ownsScaled)
            {
                scaled.Dispose();
            }
        }
    }

    /// <summary>Content type from the leading magic bytes - the archive entry name is not trusted or needed.</summary>
    internal static string Sniff(byte[] b) => b switch
    {
        [0xFF, 0xD8, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x47, 0x49, 0x46, ..] => "image/gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        [0x42, 0x4D, ..] => "image/bmp",
        _ => "application/octet-stream",
    };
}
