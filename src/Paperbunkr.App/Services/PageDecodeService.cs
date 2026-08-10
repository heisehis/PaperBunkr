using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace Paperbunkr.App.Services;

/// <summary>
/// Two-tier, virtualized, background-decoding successor to <see cref="PageImageDecoder"/>, built
/// for continuous/webtoon scroll (docs/superpowers/specs/2026-08-10-reader-polish-continuous-
/// scroll-chrome-overlays-design.md §3). Implements <see cref="IPageImageDecoder"/> so it's a
/// drop-in wherever that interface is already consumed - <see cref="PageImageDecoder"/> itself
/// stays untouched (paged mode keeps using it as-is); both share <see cref="PageDecodeCore"/>'s
/// raw-decode logic rather than duplicating it.
///
/// <b>Two-tier bitmaps:</b> <see cref="GetPage"/> returns a *display-tier* bitmap, downsampled to
/// <see cref="SetViewportWidth"/>'s last-set width immediately after decode and before caching -
/// never holds a page at native resolution once cached, the core mitigation for the documented
/// Avalonia native-bitmap-memory growth risk (issue #18498, docs/onboarding.md §8) with large
/// webtoon pages. <see cref="GetDetailPage"/> is the on-demand, higher-resolution *detail tier* -
/// decoded fresh on every call, never cached, since callers are expected to discard it once zoom
/// settles (spec §3/§4).
///
/// <b>Virtualization:</b> <see cref="SetVirtualizationWindow"/> is the explicit "which pages are
/// visible or near-visible now" signal from the layout model/render layer (docs/onboarding.md §8's
/// layout-model/render-layer split) - <see cref="GetPage"/> itself does not implicitly trim a
/// single "current index" the way <see cref="PageImageDecoder.TrimCache"/> does, since continuous
/// mode can have many pages visible at once and trimming off only the last-requested index would
/// evict pages still on screen. Anything outside the window is disposed *immediately* on the next
/// <see cref="SetVirtualizationWindow"/> call, not left for the GC.
///
/// <b>Background decode:</b> one bounded <see cref="Channel{T}"/> per priority tier (high = inside
/// the virtualization window, low = one-page prefetch just beyond each edge), a single consumer
/// loop that always drains high before low, per the threading primitive already resolved in
/// docs/open_items_resolved.md. A page still queued when the window moves past it is silently
/// skipped at dequeue time (checked against the latest window, mirroring
/// <see cref="ViewModels.ReaderScreenViewModel"/>'s own generation-check pattern) rather than
/// cancelled via a per-request token. <see cref="GetPage"/> stays synchronous regardless - it
/// decodes inline on a cache miss, so correctness never depends on the background loop having
/// already run; the loop only makes a subsequent synchronous call more likely to be a cache hit.
/// </summary>
public sealed class PageDecodeService : IPageImageDecoder
{
    /// <summary>Pages within ±this many of the virtualization window's requested bounds are still kept - matches <see cref="PageImageDecoder"/>'s own ±1 for paged mode, doubled here since continuous mode can have more than one page visible at once.</summary>
    public const int PrefetchRadius = 1;

    private const int ThumbnailLongestEdge = 200;

    private readonly ImageProvider _provider;
    private readonly Dictionary<int, AvaloniaBitmap> _displayCache = new();
    private readonly Dictionary<int, AvaloniaBitmap> _thumbnailCache = new();
    private readonly HashSet<int> _enqueued = new();
    private readonly object _sync = new();
    private readonly Channel<int> _highPriority = Channel.CreateBounded<int>(32);
    private readonly Channel<int> _lowPriority = Channel.CreateBounded<int>(32);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerLoop;
    private int _viewportWidth = int.MaxValue; // no downsampling until SetViewportWidth is called
    private HashSet<int> _window = new();

    /// <summary>
    /// Test-only seam (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-
    /// overlays-design.md §13's "injectable/controllable decode delay... not real-time races") -
    /// invoked on the background consumer loop immediately before it decodes each page, so a test
    /// can block/release specific pages deterministically to assert priority ordering under
    /// contention without sleep-based timing.
    /// </summary>
    internal Action<int>? OnBeforeBackgroundDecode { get; set; }

    private PageDecodeService(ImageProvider provider)
    {
        _provider = provider;
        _consumerLoop = Task.Run(RunConsumerLoopAsync);
    }

    /// <summary>Opens the archive at <paramref name="filePath"/>, or returns null if it can't be opened at all (missing file, unsupported format, corrupt/empty archive).</summary>
    public static PageDecodeService? TryOpen(string filePath)
    {
        var provider = PageDecodeCore.TryOpenProvider(filePath);
        return provider is null ? null : new PageDecodeService(provider);
    }

    public int PageCount => _provider.Count;

    /// <summary>Live decoded-display-tier-bitmap count - the observable form of "treat decoded-bitmap count as a hard-bounded resource" (docs/onboarding.md §8), asserted against in the memory-bound test.</summary>
    public int DecodedPageCount
    {
        get { lock (_sync) { return _displayCache.Count; } }
    }

    /// <summary>Target width for future display-tier decodes. Doesn't retroactively rescale already-cached pages - a resize mid-session just means the next decode (post-eviction) uses the new width.</summary>
    public void SetViewportWidth(int width) => _viewportWidth = Math.Max(1, width);

    public AvaloniaBitmap GetPage(int pageIndex)
    {
        lock (_sync)
        {
            if (_displayCache.TryGetValue(pageIndex, out var cached))
            {
                return cached;
            }
        }

        var decoded = DecodeDisplayTier(pageIndex);
        lock (_sync)
        {
            // Another thread (the background loop) may have decoded and cached the same page
            // while this call was decoding its own copy - keep whichever landed in the dictionary
            // first and dispose the redundant one rather than leaking it.
            if (_displayCache.TryGetValue(pageIndex, out var existing))
            {
                decoded.Dispose();
                return existing;
            }

            _displayCache[pageIndex] = decoded;
            return decoded;
        }
    }

    /// <summary>
    /// On-demand, higher-resolution detail-tier decode (spec §3) - never cached, callers discard
    /// it once zoom settles. <paramref name="targetSize"/> lets the caller ask for exactly the
    /// resolution the current zoom level needs, rather than always paying for a full-native decode.
    /// </summary>
    public AvaloniaBitmap GetDetailPage(int pageIndex, PixelSize targetSize)
    {
        using var native = PageDecodeCore.Decode(_provider, pageIndex);
        return native.CreateScaledBitmap(targetSize, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>Independent of the display-tier cache/eviction window - same shape as <see cref="PageImageDecoder.GetThumbnail"/>.</summary>
    public AvaloniaBitmap GetThumbnail(int pageIndex)
    {
        lock (_sync)
        {
            if (_thumbnailCache.TryGetValue(pageIndex, out var cached))
            {
                return cached;
            }
        }

        using AvaloniaBitmap full = PageDecodeCore.Decode(_provider, pageIndex);
        var size = full.PixelSize;
        int longest = Math.Max(size.Width, size.Height);
        double scale = longest > 0 ? Math.Min(1.0, (double)ThumbnailLongestEdge / longest) : 1.0;
        var target = new PixelSize(
            Math.Max(1, (int)Math.Round(size.Width * scale)),
            Math.Max(1, (int)Math.Round(size.Height * scale)));

        var thumbnail = full.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
        lock (_sync)
        {
            if (_thumbnailCache.TryGetValue(pageIndex, out var existing))
            {
                thumbnail.Dispose();
                return existing;
            }

            _thumbnailCache[pageIndex] = thumbnail;
            return thumbnail;
        }
    }

    /// <summary>
    /// The layout model/render layer's virtualization signal (spec §3/§13) - evicts (disposes
    /// immediately) anything outside [<paramref name="minIndex"/> - <see cref="PrefetchRadius"/>,
    /// <paramref name="maxIndex"/> + <see cref="PrefetchRadius"/>], then enqueues background decode
    /// for whatever's newly in-window but not yet cached: the window itself at high priority, the
    /// ±<see cref="PrefetchRadius"/> prefetch fringe at low priority.
    /// </summary>
    public void SetVirtualizationWindow(int minIndex, int maxIndex)
    {
        minIndex = Math.Max(0, minIndex);
        maxIndex = Math.Min(PageCount - 1, maxIndex);
        int fringeMin = Math.Max(0, minIndex - PrefetchRadius);
        int fringeMax = Math.Min(PageCount - 1, maxIndex + PrefetchRadius);

        lock (_sync)
        {
            var keep = new HashSet<int>();
            for (int i = fringeMin; i <= fringeMax; i++)
            {
                keep.Add(i);
            }

            foreach (int stale in _displayCache.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                _displayCache[stale].Dispose();
                _displayCache.Remove(stale);
            }

            _window = keep;
        }

        for (int i = minIndex; i <= maxIndex; i++)
        {
            TryEnqueue(i, _highPriority);
        }

        for (int i = fringeMin; i < minIndex; i++)
        {
            TryEnqueue(i, _lowPriority);
        }

        for (int i = maxIndex + 1; i <= fringeMax; i++)
        {
            TryEnqueue(i, _lowPriority);
        }
    }

    private void TryEnqueue(int pageIndex, Channel<int> channel)
    {
        lock (_sync)
        {
            if (_displayCache.ContainsKey(pageIndex) || _enqueued.Contains(pageIndex))
            {
                return;
            }

            _enqueued.Add(pageIndex);
        }

        channel.Writer.TryWrite(pageIndex);
    }

    private async Task RunConsumerLoopAsync()
    {
        var token = _cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_highPriority.Reader.TryRead(out int highPage))
                {
                    ProcessQueuedPage(highPage);
                    continue;
                }

                if (_lowPriority.Reader.TryRead(out int lowPage))
                {
                    ProcessQueuedPage(lowPage);
                    continue;
                }

                var highWait = _highPriority.Reader.WaitToReadAsync(token).AsTask();
                var lowWait = _lowPriority.Reader.WaitToReadAsync(token).AsTask();
                await Task.WhenAny(highWait, lowWait).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose() cancelling the loop - expected, not an error.
        }
    }

    private void ProcessQueuedPage(int pageIndex)
    {
        lock (_sync)
        {
            _enqueued.Remove(pageIndex);

            // The window moved on since this was enqueued (spec §3/§13's staleness handling) - a
            // generation-style check against the *current* window rather than a per-request
            // cancellation token, same shape as ReaderScreenViewModel._loadGeneration.
            if (!_window.Contains(pageIndex) || _displayCache.ContainsKey(pageIndex))
            {
                return;
            }
        }

        OnBeforeBackgroundDecode?.Invoke(pageIndex);

        AvaloniaBitmap decoded;
        try
        {
            decoded = DecodeDisplayTier(pageIndex);
        }
        catch
        {
            return; // one bad page doesn't break the rest - matches PageImageDecoder's GetPage/GetThumbnail contract
        }

        lock (_sync)
        {
            if (!_window.Contains(pageIndex) || _displayCache.ContainsKey(pageIndex))
            {
                decoded.Dispose();
                return;
            }

            _displayCache[pageIndex] = decoded;
        }
    }

    /// <summary>
    /// Decodes at native resolution then immediately downsamples to <see cref="_viewportWidth"/> -
    /// never returns (or holds) a native-resolution bitmap for the display tier. No-op scale when
    /// the page is already at or below the viewport width (matches CE's own <c>FitOnlyIfOversized</c>
    /// "don't upscale" precedent, ported for a different reason here - avoiding pointless GPU
    /// upload of a resized-larger copy, not avoiding visual upscaling).
    /// </summary>
    private AvaloniaBitmap DecodeDisplayTier(int pageIndex)
    {
        AvaloniaBitmap native = PageDecodeCore.Decode(_provider, pageIndex);
        var size = native.PixelSize;
        if (size.Width <= _viewportWidth)
        {
            return native;
        }

        double scale = (double)_viewportWidth / size.Width;
        var target = new PixelSize(_viewportWidth, Math.Max(1, (int)Math.Round(size.Height * scale)));
        using (native)
        {
            return native.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _consumerLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Expected: the loop's OperationCanceledException surfaces here as an AggregateException.
        }

        _cts.Dispose();

        lock (_sync)
        {
            foreach (var bitmap in _displayCache.Values)
            {
                bitmap.Dispose();
            }

            _displayCache.Clear();

            foreach (var bitmap in _thumbnailCache.Values)
            {
                bitmap.Dispose();
            }

            _thumbnailCache.Clear();
        }

        _provider.Dispose();
    }
}
