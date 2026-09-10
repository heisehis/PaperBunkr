using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using cYo.Common.Collections;
using cYo.Common.ComponentModel;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using cYo.Projects.ComicRack.Engine.IO.Provider.Readers;
using cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Archive;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// The one reader decode/cache/prefetch pipeline (docs/superpowers/specs/2026-09-08-reader-decode-
/// cache-prefetch-pipeline-design.md). Replaces <see cref="Paperbunkr.App.Services.PageImageDecoder"/>
/// (paged, synchronous) and <see cref="Paperbunkr.App.Services.PageDecodeService"/> (continuous)
/// with a single implementation used by paged, continuous and PDF reading modes.
///
/// <list type="bullet">
/// <item><b>Container opened once.</b> An <see cref="IComicAccessorSession"/> holds the archive
/// handle for the whole session (§4); page reads come off it, not a per-page reopen. Falls back to
/// the stateless <see cref="ImageProvider.GetByteImage"/> when the format has no session.</item>
/// <item><b>Three byte-bounded caches</b> (<see cref="Cache{K,T}"/>): decoded display-tier bitmaps
/// under an adaptive budget (§5), a smaller thumbnail sub-cache, and a compressed-bytes tier that
/// absorbs re-reads and feeds detail re-decode without touching the container. Eviction drops the
/// cache reference only - it never <c>Dispose()</c>s the Avalonia bitmap (this codebase's
/// CoverImageCache/LruCache crash, fixed 2026-08-09); GC reclaims once nothing references it.</item>
/// <item><b>Background decode</b> on one consumer loop draining a high- then low-priority
/// <see cref="Channel{T}"/> - decode never runs on the UI thread except a deliberate synchronous
/// <see cref="GetPage"/> on a cold cache miss (paged first view, double-page lookahead, continuous
/// first frame), the same correctness guarantee <see cref="Paperbunkr.App.Services.PageDecodeService"/> had.</item>
/// <item><b>Adaptive prefetch fringe</b> (§8.1): forward reach widens from ±2 to ±6 as a rolling
/// decode-time average shows headroom, holds tight when decode is the bottleneck.</item>
/// </list>
/// </summary>
public sealed class ReaderImagePipeline : IReaderPageSource
{
    private const int ThumbnailLongestEdge = 200;
    private const int BackFringe = 2;
    private const int MinForwardFringe = 2;
    private const int MaxForwardFringe = 6;

    private readonly ImageProvider _provider;
    private readonly ArchiveComicProvider? _archiveProvider;
    private readonly IComicAccessorSession? _session;
    private readonly ReaderMemoryBudget _budget;
    private readonly string _container;
    private readonly long _containerStamp;
    private readonly string?[] _entryNames;

    /// <summary>Bytes currently reserved for one live detail-tier bitmap (design §15 #3). While &gt; 0, <see cref="_displayCache"/>'s <c>SizeCapacity</c> is lowered by this much so real memory stays within budget. Guarded by <see cref="_sync"/>.</summary>
    private long _reservedDetailBytes;
    private int _detailReservedForPage = -1;

    private readonly Cache<PageId, ReaderBitmap> _displayCache;
    private readonly Cache<PageId, ReaderBitmap> _thumbCache;

    /// <summary>
    /// Compressed page bytes, <b>shared process-wide and outliving any one pipeline instance</b>
    /// (docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md §9): when
    /// the reader switches books and switches back - the "wrong issue, go back" case - the previous
    /// book's bytes are still here, so the first window fills with no container I/O. Keyed by
    /// <see cref="PageId"/> (container path + write-stamp), so books never alias. Byte-bounded and
    /// LRU, so closing the reader leaves at most this much lingering until anything else needs it.
    /// </summary>
    private static readonly Cache<PageId, RawPageBytes> SharedRawCache = CreateSharedRawCache();

    private static Cache<PageId, RawPageBytes> CreateSharedRawCache()
    {
        long ram;
        try { ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; } catch { ram = 8L << 30; }
        long cap = Math.Clamp(ram / 64, 16L << 20, 64L << 20);
        return new Cache<PageId, RawPageBytes>(int.MaxValue, 1) { SizeCapacity = cap, MinimalTimeInCache = 0 };
    }

    private readonly object _sync = new();

    /// <summary>
    /// Serialises every read off the container - the <see cref="IComicAccessorSession"/> (7z.dll
    /// COM / SharpZipLib / SharpCompress) and <see cref="ImageProvider.GetByteImage"/> are all
    /// single-threaded, and <see cref="DecodeDisplayTier"/> runs on both the UI thread (a
    /// synchronous <see cref="GetPage"/> cache miss) and the background consumer loop. Held only
    /// for the byte read, never across the Skia decode, so the two threads still decode in
    /// parallel.
    /// </summary>
    private readonly object _readerLock = new();

    private readonly HashSet<int> _enqueued = new();
    private readonly HashSet<int> _window = new();

    private readonly Channel<int> _highPriority = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<int> _lowPriority = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerLoop;

    private readonly double[] _recentDecodeMs = new double[8];
    private int _decodeSamples;
    private int _forwardFringe = MinForwardFringe;

    private int _viewportWidth = int.MaxValue;
    private volatile int _activePageIndex = -1;

    /// <summary>Test seam - invoked on the consumer loop just before each background decode (mirrors <see cref="Paperbunkr.App.Services.PageDecodeService.OnBeforeBackgroundDecode"/>).</summary>
    internal Action<int>? OnBeforeBackgroundDecode { get; set; }

    public event Action<int>? BackgroundDecodeCompleted;

    private ReaderImagePipeline(ImageProvider provider, IComicAccessorSession? session, ReaderMemoryBudget budget)
    {
        _provider = provider;
        _archiveProvider = provider as ArchiveComicProvider;
        _session = session;
        _budget = budget;
        _container = provider.Source ?? string.Empty;

        try
        {
            var fi = new FileInfo(_container);
            _containerStamp = fi.Exists ? fi.LastWriteTimeUtc.Ticks ^ fi.Length : 0;
        }
        catch
        {
            _containerStamp = 0;
        }

        // Archive pages are addressed by their in-container name; PDF pages (no name) by index-as-
        // string - the PdfiumAccessorSession parses that back to a page index.
        bool pdfSession = _session is not null && _archiveProvider is null;
        _entryNames = new string?[provider.Count];
        for (int i = 0; i < _entryNames.Length; i++)
        {
            _entryNames[i] = _archiveProvider?.GetFile(i)?.Name ?? (pdfSession ? i.ToString() : null);
        }

        _displayCache = MakeCache<ReaderBitmap>(budget.DisplayBytes);
        _thumbCache = MakeCache<ReaderBitmap>(budget.ThumbnailBytes);

        // Evicted decoded bitmaps: neither leak them to the GC finalizer (fast flip abandons
        // hundreds of Skia bitmaps -> native memory starves -> crash) nor dispose them on the spot
        // (a still-running page-turn animation, or a compositor frame that hasn't flushed, may
        // still be drawing one -> use-after-free -> crash). Instead they go to _pendingDispose and
        // are freed a couple of seconds later, past any animation or in-flight frame.
        _displayCache.ItemRemoved += (_, e) => QueueForDispose(e.Item);
        _thumbCache.ItemRemoved += (_, e) => QueueForDispose(e.Item);

        _consumerLoop = Task.Run(RunConsumerLoopAsync);

        // Sweeps the deferred-dispose queue on a background timer so bitmaps from the *last* flip
        // burst are still freed once the user stops (SetVirtualizationWindow / ProcessQueuedPage
        // stop firing then). Threadpool timer, not DispatcherTimer - no UI-thread dependency;
        // disposing an evicted bitmap is thread-agnostic (it's been out of every frame for seconds).
        _disposeSweep = new System.Threading.Timer(_ => DrainPendingDispose(), null, 2000, 2000);

        // Idle until SetVirtualizationWindow arms it (§15 #5).
        _fringeTimer = new System.Threading.Timer(_ => { try { RecomputeFringe(); } catch { } }, null,
            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    // A bitmap only enters this queue when it is EVICTED (scrolled out of the virtualization
    // window). The page currently on screen is always in the window, so it is never queued -
    // except for the microsecond gap between SetVirtualizationWindow evicting the *outgoing* page
    // and the compositor getting the incoming page's frame. The grace period must comfortably
    // outlast that gap and any page-turn animation (max 250ms); there is deliberately NO count
    // cap - disposing early (to bound memory) is exactly what caused the fast-flip crash.
    private const long DisposeGraceMs = 4000;
    private readonly Queue<(ReaderBitmap Bitmap, long Tick)> _pendingDispose = new();
    private readonly System.Threading.Timer _disposeSweep;

    private void QueueForDispose(ReaderBitmap? bitmap)
    {
        if (bitmap is null)
        {
            return;
        }
        lock (_pendingDispose)
        {
            _pendingDispose.Enqueue((bitmap, Environment.TickCount64));
        }
    }

    /// <summary>Frees evicted bitmaps whose grace period has elapsed. Called from the window pass and the consumer loop - both safe points, well after any frame that referenced them.</summary>
    private void DrainPendingDispose(bool all = false)
    {
        long now = Environment.TickCount64;
        while (true)
        {
            ReaderBitmap? due = null;
            lock (_pendingDispose)
            {
                if (_pendingDispose.Count > 0 && (all || now - _pendingDispose.Peek().Tick >= DisposeGraceMs))
                {
                    due = _pendingDispose.Dequeue().Bitmap;
                }
            }
            if (due is null)
            {
                break;
            }
            try { due.Dispose(); } catch { }
        }
    }

    private static Cache<PageId, T> MakeCache<T>(long sizeBytes) where T : class =>
        new(itemCapacity: int.MaxValue, sizeCapacity: 1)
        {
            SizeCapacity = Math.Max(1, sizeBytes),
            MinimalTimeInCache = 0
        };

    public static ReaderImagePipeline? TryOpen(string filePath, int? userMemoryLimitMb = null)
    {
        var provider = PageDecodeCore.TryOpenProvider(filePath);
        if (provider is null)
        {
            return null;
        }

        IComicAccessorSession? session = null;
        try
        {
            session = (provider as ArchiveComicProvider)?.TryOpenReaderSession()
                      ?? (provider as cYo.Projects.ComicRack.Engine.IO.Provider.Readers.PdfComicProvider)?.TryOpenReaderSession();
        }
        catch
        {
            session = null;
        }

        return new ReaderImagePipeline(provider, session, ReaderMemoryBudget.Resolve(userMemoryLimitMb));
    }

    public int PageCount => _provider.Count;

    public int DecodedPageCount
    {
        get { lock (_sync) { return _displayCache.Count; } }
    }

    public int ActivePageIndex => _activePageIndex;

    /// <summary>Whether the container's held-open reading session was opened (§4) - <see langword="false"/> means the pipeline is on the stateless per-page fallback.</summary>
    public bool HasSession => _session is not null;

    public void SetViewportWidth(int width) => _viewportWidth = Math.Max(1, width);

    private PageId DisplayId(int index) => new(_container, _containerStamp, index, PageTier.Display);
    private PageId ThumbId(int index) => new(_container, _containerStamp, index, PageTier.Thumbnail);

    // --- Raw compressed bytes ------------------------------------------------

    private byte[]? ReadRawBytes(int index)
    {
        var id = DisplayId(index); // raw tier is keyed the same as display; distinct cache instance
        using (var cached = SharedRawCache.LockItem(id, (Func<PageId, RawPageBytes>)null!))
        {
            if (cached?.Item is { } hit)
            {
                return hit.Bytes;
            }
        }

        byte[]? bytes = null;
        try
        {
            string? name = index >= 0 && index < _entryNames.Length ? _entryNames[index] : null;

            // Serialise the container read - the session (7z.dll COM especially) and GetByteImage
            // are single-threaded, and this runs on both the UI thread and the consumer loop.
            lock (_readerLock)
            {
                // Exotic formats (WebP/HEIF/AVIF/JP2/JXL/DjVu): the held session returns the raw
                // entry bytes, which Avalonia's Skia decoder can't read for most of these.
                // GetByteImage runs the engine's ConvertToJpeg passes, so route those through it.
                // Everything else (JPEG/PNG - the overwhelming majority) takes the fast session path.
                if (_session is not null && name is not null && !NeedsEngineConversion(name))
                {
                    bytes = _session.ReadEntryBytes(name);
                    if (bytes is not null)
                    {
                        ReaderPerfStats.Current.RecordSessionRead();
                    }
                }

                if (bytes is null)
                {
                    bytes = _provider.GetByteImage(index);
                    ReaderPerfStats.Current.RecordArchiveRead();
                }
            }
        }
        catch
        {
            bytes = null;
        }

        if (bytes is { Length: > 0 })
        {
            var captured = bytes;
            using (SharedRawCache.LockItem(id, _ => new RawPageBytes(captured))) { }
        }
        return bytes;
    }

    // --- Decode ------------------------------------------------------------------

    private ReaderBitmap DecodeDisplayTier(int index)
    {
        var sw = Stopwatch.StartNew();

        byte[]? bytes = ReadRawBytes(index);

        AvaloniaBitmap decoded = PageDecodeCore.TryDecodeBytes(bytes)
                                 ?? PageDecodeCore.Decode(_provider, index); // GDI fallback (WebP/HEIF/JXL/JP2/DjVu)

        AvaloniaBitmap display = Downsample(decoded, _viewportWidth);
        if (!ReferenceEquals(display, decoded))
        {
            // `decoded` is a private, never-shared intermediate here - safe to dispose deterministically.
            decoded.Dispose();
        }

        sw.Stop();
        RecordDecodeMs(sw.Elapsed.TotalMilliseconds);
        return new ReaderBitmap(display);
    }

    private static readonly string[] EngineConvertExtensions =
        { ".webp", ".heic", ".heif", ".avif", ".jp2", ".j2k", ".jxl", ".djvu", ".djv" };

    private static bool NeedsEngineConversion(string entryName)
    {
        int dot = entryName.LastIndexOf('.');
        if (dot < 0)
        {
            return false;
        }
        var ext = entryName.Substring(dot);
        foreach (var e in EngineConvertExtensions)
        {
            if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static AvaloniaBitmap Downsample(AvaloniaBitmap native, int targetWidth)
    {
        var size = native.PixelSize;
        if (targetWidth <= 0 || size.Width <= targetWidth)
        {
            return native;
        }

        double scale = (double)targetWidth / size.Width;
        var target = new PixelSize(targetWidth, Math.Max(1, (int)Math.Round(size.Height * scale)));
        return native.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
    }

    private void RecordDecodeMs(double ms)
    {
        lock (_sync)
        {
            _recentDecodeMs[_decodeSamples % _recentDecodeMs.Length] = ms;
            _decodeSamples++;
            int n = Math.Min(_decodeSamples, _recentDecodeMs.Length);
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += _recentDecodeMs[i];
            }
            double avg = sum / n;
            _forwardFringe = avg switch
            {
                < 15 => MaxForwardFringe,
                < 40 => 4,
                _ => MinForwardFringe
            };
        }
    }

    // --- IPageImageDecoder (synchronous) ---------------------------------------

    public AvaloniaBitmap GetPage(int pageIndex)
    {
        var id = DisplayId(pageIndex);
        var hit = PeekBitmap(_displayCache, id);
        if (hit is not null)
        {
            ReaderPerfStats.Current.RecordCacheHit();
            return hit;
        }

        ReaderPerfStats.Current.RecordCacheMiss();
        ReaderPerfStats.Current.RecordSynchronousDecode();
        ReaderBitmap decoded = DecodeDisplayTier(pageIndex);
        return StoreBitmap(_displayCache, id, decoded);
    }

    public AvaloniaBitmap GetThumbnail(int pageIndex)
    {
        var id = ThumbId(pageIndex);
        var hit = PeekBitmap(_thumbCache, id);
        if (hit is not null)
        {
            return hit;
        }

        using AvaloniaBitmap full = PageDecodeCore.Decode(_provider, pageIndex);
        var size = full.PixelSize;
        int longest = Math.Max(size.Width, size.Height);
        double scale = longest > 0 ? Math.Min(1.0, (double)ThumbnailLongestEdge / longest) : 1.0;
        var target = new PixelSize(
            Math.Max(1, (int)Math.Round(size.Width * scale)),
            Math.Max(1, (int)Math.Round(size.Height * scale)));
        var thumb = new ReaderBitmap(full.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality));
        return StoreBitmap(_thumbCache, id, thumb);
    }

    public AvaloniaBitmap GetDetailPage(int pageIndex, PixelSize targetSize)
    {
        ReserveDetailBudget(pageIndex, targetSize);

        byte[]? bytes = ReadRawBytes(pageIndex);
        using AvaloniaBitmap native = PageDecodeCore.TryDecodeBytes(bytes) ?? PageDecodeCore.Decode(_provider, pageIndex);
        return native.CreateScaledBitmap(targetSize, BitmapInterpolationMode.HighQuality);
    }

    public void ReleaseDetail() => ReleaseDetailBudget();

    /// <summary>
    /// Reserves <c>w*h*4</c> bytes for a detail bitmap against the display-cache budget (design
    /// §15 #3): drop <see cref="_displayCache"/>'s <c>SizeCapacity</c> by the reservation, which
    /// evicts LRU (out-of-window) decoded pages to fit. The active page is pinned across the drop
    /// so <see cref="Cache{K,T}"/>'s <c>Trim</c> can't take it. Best-effort - if the window itself
    /// exceeds the reduced cap the detail decode still proceeds (a brief overshoot beats a failed
    /// zoom). Any prior reservation is released first (one detail bitmap live at a time).
    /// </summary>
    private void ReserveDetailBudget(int pageIndex, PixelSize targetSize)
    {
        ReleaseDetailBudget();

        long reserve;
        try { reserve = checked((long)targetSize.Width * targetSize.Height * 4); }
        catch (OverflowException) { reserve = _budget.DisplayBytes; }
        if (reserve <= 0)
        {
            return;
        }

        lock (_sync)
        {
            IItemLock<ReaderBitmap>? pin = pageIndex >= 0
                ? _displayCache.LockItem(DisplayId(pageIndex), (Func<PageId, ReaderBitmap>)null!)
                : null;
            try
            {
                _displayCache.SizeCapacity = Math.Max(1L, _budget.DisplayBytes - reserve);
            }
            finally
            {
                pin?.Dispose();
            }
            _reservedDetailBytes = reserve;
            _detailReservedForPage = pageIndex;
        }
    }

    private void ReleaseDetailBudget()
    {
        lock (_sync)
        {
            if (_reservedDetailBytes <= 0)
            {
                return;
            }
            _reservedDetailBytes = 0;
            _detailReservedForPage = -1;
            _displayCache.SizeCapacity = Math.Max(1L, _budget.DisplayBytes);
        }
    }

    public AvaloniaBitmap? TryGetCachedPage(int pageIndex)
    {
        var hit = PeekBitmap(_displayCache, DisplayId(pageIndex));
        if (hit is not null)
        {
            ReaderPerfStats.Current.RecordCacheHit();
        }
        else
        {
            ReaderPerfStats.Current.RecordCacheMiss();
        }
        return hit;
    }

    private AvaloniaBitmap? PeekBitmap(Cache<PageId, ReaderBitmap> cache, PageId id)
    {
        lock (_sync)
        {
            var lease = cache.LockItem(id, (Func<PageId, ReaderBitmap>)null!);
            if (lease is null)
            {
                return null;
            }
            using (lease)
            {
                return lease.Item.Bitmap;
            }
        }
    }

    private AvaloniaBitmap StoreBitmap(Cache<PageId, ReaderBitmap> cache, PageId id, ReaderBitmap decoded)
    {
        lock (_sync)
        {
            var stored = cache.LockItem(id, _ => decoded);
            if (stored is null)
            {
                return decoded.Bitmap;
            }
            using (stored)
            {
                if (!ReferenceEquals(stored.Item, decoded))
                {
                    decoded.Dispose(); // lost the single-flight race
                }
                return stored.Item.Bitmap;
            }
        }
    }

    // --- Virtualization window + prefetch ------------------------------------

    /// <summary>Trailing-edge debounce for the low-priority prefetch-fringe recompute during a rapid sequential flip (design §15 #5). The high-priority window still updates on every call.</summary>
    private const int FringeDebounceMs = 30;
    private int _pendingMin = -1;
    private int _pendingMax = -1;
    private readonly System.Threading.Timer _fringeTimer;

    /// <summary><see cref="Environment.TickCount64"/> before which <see cref="RecomputeFringe"/> defers itself (a page-turn transition is animating). 0 = not suppressed.</summary>
    private long _fringeSuppressedUntilTick;

    public void SuppressFringePrefetch(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return;
        }
        _fringeSuppressedUntilTick = Environment.TickCount64 + milliseconds;
        // Make sure a pass is scheduled to run once the hold lifts.
        _fringeTimer.Change(milliseconds + 10, System.Threading.Timeout.Infinite);
    }

    /// <summary>Test seam (design §12.4): fires at the end of each debounced <see cref="RecomputeFringe"/> pass.</summary>
    internal Action? OnFringeRecomputed { get; set; }

    public void SetVirtualizationWindow(int minIndex, int maxIndex)
    {
        minIndex = Math.Max(0, minIndex);
        maxIndex = Math.Min(PageCount - 1, maxIndex);
        if (maxIndex < minIndex)
        {
            return;
        }

        _activePageIndex = (minIndex + maxIndex) / 2;

        // A page turn invalidates any detail-tier reservation held for the old page (design §15 #3).
        // PageCanvas also calls ReleaseDetail() explicitly on zoom-out; this is the belt-and-braces
        // path for a straight page turn.
        int reservedFor;
        lock (_sync) { reservedFor = _detailReservedForPage; }
        if (reservedFor >= 0 && reservedFor != _activePageIndex)
        {
            ReleaseDetailBudget();
        }

        // Widest bound the debounced fringe pass could land on - evict against this now so a
        // still-pending precise pass never leaves an out-of-fringe page resident beyond one
        // debounce interval, without churning the exact fringe every call.
        int safeMin = Math.Max(0, minIndex - BackFringe);
        int safeMax = Math.Min(PageCount - 1, maxIndex + MaxForwardFringe);

        DrainPendingDispose();

        lock (_sync)
        {
            _pendingMin = minIndex;
            _pendingMax = maxIndex;

            foreach (var key in _displayCache.GetKeys())
            {
                if (key.Index < safeMin || key.Index > safeMax)
                {
                    _displayCache.RemoveItem(key);
                }
            }

            // The window gates ProcessQueuedPage; the high-priority pages enqueued just below must
            // be in it. The debounced pass narrows it to the precise fringe.
            for (int i = safeMin; i <= safeMax; i++)
            {
                _window.Add(i);
            }
            _enqueued.RemoveWhere(i => i < safeMin || i > safeMax);
        }

        // Immediate: the page(s) actually on screen, high priority, every call.
        for (int i = minIndex; i <= maxIndex; i++)
        {
            TryEnqueue(i, _highPriority);
        }

        // Debounced: the low-priority fringe recompute + enqueue.
        _fringeTimer.Change(FringeDebounceMs, System.Threading.Timeout.Infinite);
    }

    private void RecomputeFringe()
    {
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        long suppressedFor = _fringeSuppressedUntilTick - Environment.TickCount64;
        if (suppressedFor > 0)
        {
            // A transition is animating - defer the whole fringe pass (eviction + low-priority
            // enqueue) until it finishes. The visible window still decodes: SetVirtualizationWindow's
            // immediate path keeps _window wide enough and evicts against the widest bound.
            _fringeTimer.Change((int)Math.Min(suppressedFor + 10, 2000), System.Threading.Timeout.Infinite);
            return;
        }

        DrainPendingDispose();

        int minIndex, maxIndex, fringeMin, fringeMax;
        lock (_sync)
        {
            // Read the pending window and rebuild _window under one lock so an interleaved
            // SetVirtualizationWindow can't have its just-enqueued high-priority pages wiped out.
            minIndex = _pendingMin;
            maxIndex = _pendingMax;
            if (minIndex < 0 || maxIndex < 0)
            {
                return;
            }

            fringeMin = Math.Max(0, minIndex - BackFringe);
            fringeMax = Math.Min(PageCount - 1, maxIndex + _forwardFringe);

            foreach (var key in _displayCache.GetKeys())
            {
                if (key.Index < fringeMin || key.Index > fringeMax)
                {
                    _displayCache.RemoveItem(key);
                }
            }

            _window.Clear();
            for (int i = fringeMin; i <= fringeMax; i++)
            {
                _window.Add(i);
            }
            _enqueued.RemoveWhere(i => i < fringeMin || i > fringeMax);
        }

        for (int i = fringeMin; i < minIndex; i++)
        {
            TryEnqueue(i, _lowPriority);
        }
        for (int i = maxIndex + 1; i <= fringeMax; i++)
        {
            TryEnqueue(i, _lowPriority);
        }

        OnFringeRecomputed?.Invoke();
    }

    private void TryEnqueue(int pageIndex, Channel<int> channel)
    {
        lock (_sync)
        {
            if (_enqueued.Contains(pageIndex) || _displayCache.IsCached(DisplayId(pageIndex)))
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
                if (_highPriority.Reader.TryRead(out int high))
                {
                    ProcessQueuedPage(high);
                    continue;
                }
                if (_lowPriority.Reader.TryRead(out int low))
                {
                    ProcessQueuedPage(low);
                    continue;
                }

                var hi = _highPriority.Reader.WaitToReadAsync(token).AsTask();
                var lo = _lowPriority.Reader.WaitToReadAsync(token).AsTask();
                await Task.WhenAny(hi, lo).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // never let the loop thread take down the process
        }
    }

    private void ProcessQueuedPage(int pageIndex)
    {
        lock (_sync)
        {
            _enqueued.Remove(pageIndex);
            if (!_window.Contains(pageIndex) || _displayCache.IsCached(DisplayId(pageIndex)))
            {
                return;
            }
        }

        DrainPendingDispose();
        OnBeforeBackgroundDecode?.Invoke(pageIndex);
        ReaderPerfStats.Current.RecordBackgroundDecode();

        ReaderBitmap decoded;
        try
        {
            decoded = DecodeDisplayTier(pageIndex);
        }
        catch
        {
            return;
        }

        bool landed;
        lock (_sync)
        {
            if (!_window.Contains(pageIndex))
            {
                decoded.Dispose();
                return;
            }
            var stored = _displayCache.LockItem(DisplayId(pageIndex), _ => decoded);
            landed = stored is not null;
            if (stored is not null)
            {
                using (stored)
                {
                    if (!ReferenceEquals(stored.Item, decoded))
                    {
                        decoded.Dispose();
                    }
                }
            }
            else
            {
                decoded.Dispose();
            }
        }

        if (landed)
        {
            try { BackgroundDecodeCompleted?.Invoke(pageIndex); } catch { }
        }
    }

    public void Dispose()
    {
        _fringeTimer.Dispose();
        _disposeSweep.Dispose();
        _cts.Cancel();
        try { _consumerLoop.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _cts.Dispose();

        lock (_sync)
        {
            _displayCache.Dispose(); // ItemRemoved queues every cached bitmap for deferred dispose
            _thumbCache.Dispose();
            // SharedRawCache is process-wide (§9 back-nav retention) - not disposed here.
        }

        // The reader may still be showing this book's last page for a beat while the next one
        // loads (ReaderScreenViewModel.Load's documented gap) - free the bitmaps a few seconds
        // out, detached, well past any lingering frame.
        var queue = _pendingDispose;
        _ = Task.Delay(TimeSpan.FromSeconds(4)).ContinueWith(_ =>
        {
            lock (queue)
            {
                while (queue.Count > 0)
                {
                    try { queue.Dequeue().Bitmap.Dispose(); } catch { }
                }
            }
        }, TaskScheduler.Default);

        _session?.Dispose();
        _provider.Dispose();
    }
}
