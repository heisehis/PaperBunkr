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
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;
using SkiaSharp;
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
public sealed class ReaderImagePipeline : IReaderPageSource, IReaderPageProcessing
{
    private const int ThumbnailLongestEdge = 200;
    private const int BackFringe = 2;
    private const int MinForwardFringe = 2;
    private const int MaxForwardFringe = 6;

    /// <summary>
    /// A page whose height/width ratio exceeds this is a webtoon/manhwa "strip" (docs/superpowers/
    /// specs/2026-09-09-reader-webtoon-strip-band-decode-design.md §3) - a normal manga page is
    /// ~1.5, a double-page spread ~0.77, a strip is 8-30. Only strips take the band-decode path;
    /// everything else is unaffected.
    /// </summary>
    private const double StripAspectThreshold = 3.0;

    /// <summary>
    /// Fixed **source**-pixel band size for strip decode (design §4.1.1, rev 3/4 - reverted from an
    /// earlier display-pixel-sized draft once the scanline-decode session (<see cref="StripDecodeSession"/>)
    /// turned out to need band boundaries that don't move with zoom). Also the chunk size the
    /// reverse-scroll-restart skip is sliced into under <see cref="_readerLock"/> (design rev 4), so
    /// a waiting synchronous cold-miss is never blocked longer than one chunk's skip.
    /// </summary>
    internal const int BandHeight = 4096;

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

    private readonly HashSet<PipelineRequest> _enqueued = new();
    private readonly HashSet<int> _window = new();

    /// <summary>Per-page strip-or-not verdict (design §3/§4.2), cached since callers ask every window/eviction pass. Guarded by <see cref="_sync"/>.</summary>
    private readonly Dictionary<int, bool> _stripVerdict = new();

    /// <summary>Header-declared page sizes learned via <see cref="RequestPageSize"/>/<see cref="PeekPageSize"/> (design §4.3) - separate from <see cref="PageCanvas"/>'s own <c>_knownPageSizes</c> (the pipeline doesn't reach into the view layer); that one mirrors this one via <see cref="PageSizeAvailable"/>. Guarded by <see cref="_sync"/>.</summary>
    private readonly Dictionary<int, PixelSize> _knownPageSize = new();

    public event Action<int, PixelSize>? PageSizeAvailable;

    /// <summary>
    /// One queued unit of background work (design §4.2/§4.3, rev 5) - a page's whole-bitmap decode,
    /// a header-only size peek, or a strip band decode, all sharing one high/low-priority queue and
    /// one <see cref="_window"/>/<see cref="_enqueued"/> staleness-check mechanism rather than three
    /// parallel ones. <see cref="Band"/> is meaningful only when <see cref="Kind"/> is
    /// <see cref="RequestKind.Band"/>.
    /// </summary>
    private enum RequestKind { WholePage, PageSize, Band }

    private readonly record struct PipelineRequest(RequestKind Kind, int PageIndex, int Band = 0);

    private readonly Channel<PipelineRequest> _highPriority = Channel.CreateUnbounded<PipelineRequest>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<PipelineRequest> _lowPriority = Channel.CreateUnbounded<PipelineRequest>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerLoop;

    /// <summary>Decoded strip bands (design §4.1) - a separate cache instance from <see cref="_displayCache"/>, sized as a fraction of the same display budget (tuned during the benchmark step; a strip in view rarely needs more than a handful of bands resident at once, unlike whole pages which can span the full prefetch fringe).</summary>
    private readonly Cache<StripBandId, ReaderBitmap> _bandCache;

    /// <summary>One open forward-only scanline session per strip page currently within reach of the window (design §4.1) - <see cref="StripDecodeSessionEntry.Lock"/> is scoped to that one page's session, never the pipeline-wide <see cref="_readerLock"/> (rev 5 - see that field's own doc comment for why). Guarded by <see cref="_sync"/> for get/add/remove; the entry's own lock guards the session's actual scanline calls.</summary>
    private readonly Dictionary<int, StripDecodeSessionEntry> _stripSessions = new();

    /// <summary>Once known: whether a strip page's format actually supports band decode (JPEG, in practice - PNG doesn't, design §4.1's rev 5 finding) or must fall back to whole-page decode. Unlike <see cref="_stripVerdict"/> (free from the size peek's already-read header), this needs an actual <see cref="StripDecodeSession.TryCreate"/> attempt, so it's populated lazily the first time a band is requested for that page. Guarded by <see cref="_sync"/>.</summary>
    private readonly Dictionary<int, bool> _stripBandable = new();

    private sealed class StripDecodeSessionEntry
    {
        public StripDecodeSessionEntry(StripDecodeSession session)
        {
            Session = session;
        }

        public StripDecodeSession Session { get; }
        public readonly object Lock = new();
    }

    private readonly double[] _recentDecodeMs = new double[8];
    private int _decodeSamples;
    private int _forwardFringe = MinForwardFringe;

    private int _viewportWidth = int.MaxValue;
    private volatile int _activePageIndex = -1;

    /// <summary>Test seam - invoked on the consumer loop just before each background decode (mirrors <see cref="Paperbunkr.App.Services.PageDecodeService.OnBeforeBackgroundDecode"/>).</summary>
    internal Action<int>? OnBeforeBackgroundDecode { get; set; }

    /// <summary>Test seam, same shape as <see cref="OnBeforeBackgroundDecode"/> - invoked on the consumer loop just before each header-only page-size peek (design §4.3), so a test can observe it running off the calling thread and count how many times it actually ran.</summary>
    internal Action<int>? OnBeforePageSizePeek { get; set; }

    public event Action<int>? BackgroundDecodeCompleted;

    private ReaderImagePipeline(ImageProvider provider, IComicAccessorSession? session, ReaderMemoryBudget budget, long? containerStampOverride = null)
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

        // A provider with no file behind it (a remote library's issue) has no mtime/size to tell "same book" from "different
        // book at the same address", so the caller supplies a stamp that is unique to this open. Without it the process-wide
        // SharedRawCache would serve one open's page bytes to the next - stale content after the host replaces the book, or
        // after a Relink re-keys the id to a different one.
        if (containerStampOverride is long stamp)
        {
            _containerStamp = stamp;
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
        // A quarter of the display budget (design §4.1) - a strip in view rarely needs more than a
        // handful of bands resident at once (design's own eviction range is +-2*BandHeight), unlike
        // whole pages which can span the full prefetch fringe.
        _bandCache = new Cache<StripBandId, ReaderBitmap>(itemCapacity: int.MaxValue, sizeCapacity: 1)
        {
            SizeCapacity = Math.Max(1, budget.DisplayBytes / 4),
            MinimalTimeInCache = 0
        };

        // Evicted decoded bitmaps: neither leak them to the GC finalizer (fast flip abandons
        // hundreds of Skia bitmaps -> native memory starves -> crash) nor dispose them on the spot
        // (a still-running page-turn animation, or a compositor frame that hasn't flushed, may
        // still be drawing one -> use-after-free -> crash). Instead they go to _pendingDispose and
        // are freed a couple of seconds later, past any animation or in-flight frame.
        _displayCache.ItemRemoved += (_, e) => QueueForDispose(e.Item);
        _thumbCache.ItemRemoved += (_, e) => QueueForDispose(e.Item);
        _bandCache.ItemRemoved += (_, e) => QueueForDispose(e.Item);

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

    /// <summary>
    /// Opens the pipeline over a provider that isn't a file on disk - a remote library's issue
    /// (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md section 7.3). The session, if
    /// any, supplies concurrent page reads exactly as an archive or PDF session does; everything else
    /// (decode workers, caches, prefetch, strip bands) is the same code path a local file takes.
    /// </summary>
    internal static ReaderImagePipeline OpenProvider(ImageProvider provider, IComicAccessorSession? session, int? userMemoryLimitMb = null) =>
        new(provider, session, ReaderMemoryBudget.Resolve(userMemoryLimitMb), containerStampOverride: NextOpenStamp());

    private static long s_openStamp = DateTime.UtcNow.Ticks;

    /// <summary>Unique for the life of the process (a counter seeded from the clock, so it also can't collide with a real file's mtime^size stamp in practice).</summary>
    private static long NextOpenStamp() => Interlocked.Increment(ref s_openStamp);

    public int PageCount => _provider.Count;

    public int DecodedPageCount
    {
        get { lock (_sync) { return _displayCache.Count; } }
    }

    /// <summary>Test seam (design §6's memory-bound test): total bytes currently resident in <see cref="_bandCache"/>, the same observable "decoded [bands] are a hard-bounded resource" signal <see cref="DecodedPageCount"/> is for whole pages.</summary>
    internal long DecodedBandBytes
    {
        get { lock (_sync) { return _bandCache.Size; } }
    }

    public int ActivePageIndex => _activePageIndex;

    /// <summary>Whether the container's held-open reading session was opened (§4) - <see langword="false"/> means the pipeline is on the stateless per-page fallback.</summary>
    public bool HasSession => _session is not null;

    public void SetViewportWidth(int width) => _viewportWidth = Math.Max(1, width);

    /// <summary>The width future display-tier decodes downsample to (<see cref="int.MaxValue"/> until <see cref="SetViewportWidth"/> is first called). The next-issue stager copies it onto the pipeline it stages.</summary>
    internal int ViewportWidth => _viewportWidth;

    private PageId DisplayId(int index) => new(_container, _containerStamp, index, PageTier.Display, PageVariant(index));
    private PageId ThumbId(int index) => new(_container, _containerStamp, index, PageTier.Thumbnail);

    /// <summary>The compressed-bytes tier is keyed without a variant: processing changes the decoded pixels, never the file's bytes.</summary>
    private PageId RawId(int index) => new(_container, _containerStamp, index, PageTier.Display);

    // --- Page processing (auto-levels, auto-crop) --------------------------------

    private volatile PageProcessingSettings _processingSettings = PageProcessingSettings.Off;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int Index, int Variant), PageProcessingResult> _processed = new();

    private sealed record PageProcessingSettings(PageProcessingOptions Options, IReadOnlyDictionary<int, PageCropMode> CropOverrides)
    {
        public static readonly PageProcessingSettings Off = new(PageProcessingOptions.None, new Dictionary<int, PageCropMode>());
    }

    public PageProcessingOptions Processing => _processingSettings.Options;

    public void SetProcessing(PageProcessingOptions options, IReadOnlyDictionary<int, PageCropMode>? cropOverrides)
    {
        _processingSettings = new PageProcessingSettings(options, cropOverrides is null ? new Dictionary<int, PageCropMode>() : new Dictionary<int, PageCropMode>(cropOverrides));
        _processed.Clear();

        // Pages cached under another processing are dead weight now (never found again): drop them so they do not crowd the byte budget.
        lock (_sync)
        {
            foreach (var key in _displayCache.GetKeys())
            {
                if (key.Variant != PageVariant(key.Index))
                {
                    _displayCache.RemoveItem(key);
                }
            }
        }
    }

    public PageCropRect? DetectCrop(int pageIndex)
    {
        byte[]? bytes = ReadRawBytes(pageIndex);
        using AvaloniaBitmap decoded = PageDecodeCore.TryDecodeBytes(bytes) ?? PageDecodeCore.Decode(_provider, pageIndex);
        using var image = SkiaBitmapConverter.ToSkImage(decoded);
        using var page = SKBitmap.FromImage(image);
        return page is null ? null : PageCropDetector.Detect(PageLumaGrid.From(page));
    }

    /// <summary>Whether the page is cropped: its own override if it has one, else the setting.</summary>
    private bool CropsPage(int index)
    {
        var settings = _processingSettings;
        return settings.CropOverrides.TryGetValue(index, out var mode) && mode != PageCropMode.Auto
            ? mode == PageCropMode.Always
            : settings.Options.AutoCrop;
    }

    /// <summary>Which processing the display-tier bitmap of a page gets: bit 0 auto-levels, bit 1 auto-crop. Part of the cache identity, so a setting or override change decodes the page again.</summary>
    internal int PageVariant(int index) => (_processingSettings.Options.AutoLevels ? 1 : 0) | (CropsPage(index) ? 2 : 0);

    /// <summary>
    /// Applies auto-crop and auto-levels to a freshly decoded page. Returns <paramref name="decoded"/> itself when there is nothing to do (the caller keeps ownership of it), else a new bitmap (the caller
    /// disposes <paramref name="decoded"/> if it is its own private intermediate). What was decided is remembered per page and variant so the detail tier crops and stretches exactly the same way. Webtoon
    /// strips are left alone: they have no scan borders and decode in bands.
    /// </summary>
    private AvaloniaBitmap ProcessDecoded(AvaloniaBitmap decoded, int index)
    {
        int variant = PageVariant(index);
        if (variant == 0 || IsStrip(index))
        {
            return decoded;
        }

        try
        {
            using var image = SkiaBitmapConverter.ToSkImage(decoded);
            using var page = SKBitmap.FromImage(image);
            if (page is null)
            {
                return decoded;
            }

            var settings = _processingSettings;
            if (!_processed.TryGetValue((index, variant), out var result))
            {
                result = PageImageProcessor.Analyze(page, settings.Options.AutoLevels, CropsPage(index));
                _processed[(index, variant)] = result;
            }

            if (result.IsNothing)
            {
                return decoded;
            }

            using var processed = PageImageProcessor.Apply(page, result);
            return SkiaBitmapConverter.ToImmutableBitmap(processed);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Processing is a nicety: a page that cannot be analysed is shown as it is.
            return decoded;
        }
    }

    // --- Raw compressed bytes ------------------------------------------------

    private byte[]? ReadRawBytes(int index)
    {
        var id = RawId(index); // raw tier is keyed the same as display (without the processing variant); distinct cache instance
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
                        Stats.RecordSessionRead();
                    }
                }

                if (bytes is null)
                {
                    bytes = _provider.GetByteImage(index);
                    Stats.RecordArchiveRead();
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

        // Auto-crop and auto-levels work on the full-size decode, before the downscale (the downscale then sees only the part that will be shown).
        var processed = ProcessDecoded(decoded, index);
        if (!ReferenceEquals(processed, decoded))
        {
            decoded.Dispose();
            decoded = processed;
        }

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

    // --- Strip classification / header-only size peek (design §3/§4.3) -----------

    /// <summary>
    /// Reads a page's true pixel size straight off its encoded header - <b>no pixel decode</b>
    /// (design §4.3). Opens an <see cref="SKCodec"/> on the already-cached raw bytes (§4/§9 - costs
    /// no container I/O beyond whatever already fetching those bytes cost) and reads
    /// <see cref="SKCodec.Info"/> only. Returns <see langword="null"/> for anything the header can't
    /// be read from (missing bytes, a format `SKCodec.Create` doesn't recognise, etc.) - callers
    /// fall back to whatever estimate they already had, same as every other "couldn't decode this
    /// one page" tolerance in this pipeline.
    /// </summary>
    internal PixelSize? PeekPageSize(int pageIndex)
    {
        byte[]? bytes = ReadRawBytes(pageIndex);
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(bytes));
            if (codec is null)
            {
                return null;
            }
            var info = codec.Info;
            return info.Width > 0 && info.Height > 0 ? new PixelSize(info.Width, info.Height) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the page is a webtoon/manhwa "strip" (design §3) - cached per page index
    /// (<see cref="_stripVerdict"/>, guarded by <see cref="_sync"/>) since <see cref="PeekPageSize"/>
    /// re-reads the header on every call and callers (§4.2's window/eviction passes) ask this every
    /// frame. A page whose header can't be read (<see cref="PeekPageSize"/> returns
    /// <see langword="null"/>) is treated as not-a-strip - it falls back to the ordinary whole-page
    /// path, same as any other undecodable-header case elsewhere in this pipeline.
    /// </summary>
    internal bool IsStrip(int pageIndex)
    {
        lock (_sync)
        {
            if (_stripVerdict.TryGetValue(pageIndex, out bool cached))
            {
                return cached;
            }
        }

        var size = PeekPageSize(pageIndex);
        bool verdict = size is { Width: > 0 } s && s.Height / (double)s.Width > StripAspectThreshold;

        lock (_sync)
        {
            _stripVerdict[pageIndex] = verdict;
        }
        return verdict;
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
            Stats.RecordCacheHit();
            return hit;
        }

        Stats.RecordCacheMiss();
        Stats.RecordSynchronousDecode();
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
        using AvaloniaBitmap decodedNative = PageDecodeCore.TryDecodeBytes(bytes) ?? PageDecodeCore.Decode(_provider, pageIndex);
        var native = ProcessDecoded(decodedNative, pageIndex);   // the same crop and levels the display tier got
        try
        {
            return native.CreateScaledBitmap(targetSize, BitmapInterpolationMode.HighQuality);
        }
        finally
        {
            if (!ReferenceEquals(native, decodedNative))
            {
                native.Dispose();
            }
        }
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

    /// <summary>Whether this pipeline feeds the process-wide <see cref="ReaderPerfStats.Current"/> overlay numbers. False while the pipeline is only staged for the next issue (design 2026-09-25 A), so it cannot pollute the numbers of the book being read.</summary>
    internal bool RecordStats { get; set; } = true;

    private static readonly ReaderPerfStats DiscardedStats = new();

    private ReaderPerfStats Stats => RecordStats ? ReaderPerfStats.Current : DiscardedStats;

    /// <summary>
    /// Pages whose first lookup since entering the window has already been counted (guarded by <see cref="_sync"/>). The continuous
    /// canvas peeks every visible page every frame; counting each peek made the hit ratio meaningless, so a page is counted once -
    /// hit = it was ready the first time anything looked, miss = it was not (design 2026-09-25 B5).
    /// </summary>
    private readonly HashSet<int> _lookedUp = new();

    /// <summary>Timestamp (<see cref="Stopwatch.GetTimestamp"/>) at which a page entered the window, for decode-latency metrics; guarded by <see cref="_sync"/>.</summary>
    private readonly Dictionary<int, long> _windowEntryTimestamps = new();

    /// <summary>Forgets per-page metric state for pages outside [<paramref name="low"/>, <paramref name="high"/>]. Caller holds <see cref="_sync"/>.</summary>
    private void PruneMetricState(int low, int high)
    {
        _lookedUp.RemoveWhere(i => i < low || i > high);
        if (_windowEntryTimestamps.Count > 0)
        {
            foreach (int page in _windowEntryTimestamps.Keys.Where(i => i < low || i > high).ToList())
            {
                _windowEntryTimestamps.Remove(page);
            }
        }
    }

    public AvaloniaBitmap? TryGetCachedPage(int pageIndex)
    {
        var hit = PeekBitmap(_displayCache, DisplayId(pageIndex));
        bool first;
        lock (_sync) { first = _lookedUp.Add(pageIndex); }
        if (first)
        {
            if (hit is not null)
            {
                Stats.RecordCacheHit();
            }
            else
            {
                Stats.RecordCacheMiss();
            }
        }
        return hit;
    }

    public AvaloniaBitmap? TryGetCachedBand(int pageIndex, int band)
    {
        var id = new StripBandId(_container, _containerStamp, pageIndex, band);
        lock (_sync)
        {
            var lease = _bandCache.LockItem(id, (Func<StripBandId, ReaderBitmap>)null!);
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

    /// <summary>Interval of the low-priority fringe pass while a continuous scroll is ongoing (design 2026-09-25 B3). A trailing debounce never fired mid-scroll: every frame (16 ms) re-armed a 30 ms timer.</summary>
    private const int FringeThrottleMs = 100;

    /// <summary>1 while a throttled fringe pass is already scheduled; the pass clears it. Only the sustained-scroll path uses it.</summary>
    private int _fringeThrottleArmed;

    /// <summary>How many pages beyond the layout radius, in the scroll direction, are decoded at high priority.</summary>
    private const int ScrollLookAheadExtra = 1;

    public void SetVirtualizationWindow(int minIndex, int maxIndex) => SetVirtualizationWindowCore(minIndex, maxIndex, null);

    public void SetVirtualizationWindow(int minIndex, int maxIndex, ScrollWindowHint hint) => SetVirtualizationWindowCore(minIndex, maxIndex, hint);

    /// <summary>
    /// The order in which a window's pages are queued for high-priority decode. Without a hint (paged mode) it is simply ascending. With
    /// one, the pages actually on screen come first, nearest the visible centre first; then the pages beyond them in the scroll direction
    /// (nearest first, plus <see cref="ScrollLookAheadExtra"/> more), then the ones behind. FIFO decode means this order is the decode order.
    /// </summary>
    internal static IReadOnlyList<int> OrderWindow(int minIndex, int maxIndex, int pageCount, ScrollWindowHint? hint)
    {
        var order = new List<int>();
        if (hint is not { } h)
        {
            for (int i = minIndex; i <= maxIndex; i++)
            {
                order.Add(i);
            }

            return order;
        }

        int visibleMin = Math.Clamp(h.VisibleMin, minIndex, maxIndex);
        int visibleMax = Math.Clamp(h.VisibleMax, visibleMin, maxIndex);
        double centre = (visibleMin + visibleMax) / 2.0;
        int direction = Math.Sign(h.Direction);

        var visible = new List<int>();
        for (int i = visibleMin; i <= visibleMax; i++)
        {
            visible.Add(i);
        }

        // Nearest the centre first; on a tie the one in the scroll direction wins.
        visible.Sort((a, b) =>
        {
            int byDistance = Math.Abs(a - centre).CompareTo(Math.Abs(b - centre));
            return byDistance != 0 ? byDistance : direction >= 0 ? a.CompareTo(b) : b.CompareTo(a);
        });
        order.AddRange(visible);

        var ahead = new List<int>();
        var behind = new List<int>();
        if (direction >= 0)
        {
            for (int i = visibleMax + 1; i <= maxIndex; i++) { ahead.Add(i); }
            for (int i = visibleMin - 1; i >= minIndex; i--) { behind.Add(i); }
            if (direction > 0)
            {
                for (int extra = 1; extra <= ScrollLookAheadExtra && maxIndex + extra < pageCount; extra++) { ahead.Add(maxIndex + extra); }
            }
        }
        else
        {
            for (int i = visibleMin - 1; i >= minIndex; i--) { ahead.Add(i); }
            for (int i = visibleMax + 1; i <= maxIndex; i++) { behind.Add(i); }
            for (int extra = 1; extra <= ScrollLookAheadExtra && minIndex - extra >= 0; extra++) { ahead.Add(minIndex - extra); }
        }

        order.AddRange(ahead);
        order.AddRange(behind);
        return order;
    }

    private void SetVirtualizationWindowCore(int minIndex, int maxIndex, ScrollWindowHint? hint)
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
            EvictStripSessionsAndBandsOutside(safeMin, safeMax);
            PruneMetricState(safeMin, safeMax);

            // Decode-latency clock for every page of the requested window (design 2026-09-25 B5); a page already stamped keeps its earlier time.
            long enteredAt = Stopwatch.GetTimestamp();
            for (int i = minIndex; i <= maxIndex; i++)
            {
                _windowEntryTimestamps.TryAdd(i, enteredAt);
            }

            // The window gates ProcessQueuedRequest; the high-priority requests enqueued just below
            // must be in it. The debounced pass narrows it to the precise fringe.
            for (int i = safeMin; i <= safeMax; i++)
            {
                _window.Add(i);
            }
            _enqueued.RemoveWhere(r => r.PageIndex < safeMin || r.PageIndex > safeMax);
        }

        // Immediate: the page(s) actually on screen, high priority, every call - visible pages first when the caller says which they are.
        foreach (int i in OrderWindow(minIndex, maxIndex, PageCount, hint))
        {
            // A page already known to be a bandable strip is routed through SetStripBandWindow
            // instead (design §4.2) - PageCanvas calls that separately with the actual visible
            // sub-range, which this whole-page-granular method has no way to express. A page not
            // yet known to be a strip (or known-and-not-a-strip, or known-strip-but-unbandable)
            // still gets the ordinary whole-page request as a safe default for this pass.
            if (!ShouldRouteAsStrip(i))
            {
                TryEnqueue(new PipelineRequest(RequestKind.WholePage, i), _highPriority);
            }

            // A size peek (design §4.3) for a visible page whose true size isn't known yet - high
            // priority too, since an inaccurate estimate for something actually on screen is the
            // exact lurch this exists to avoid, not just a background nicety.
            bool sizeKnown;
            lock (_sync) { sizeKnown = _knownPageSize.ContainsKey(i); }
            if (!sizeKnown)
            {
                TryEnqueue(new PipelineRequest(RequestKind.PageSize, i), _highPriority);
            }
        }

        // The low-priority fringe recompute + enqueue. Paged mode keeps the trailing debounce (a burst of rapid flips should enqueue once, for
        // the final page). A continuous scroll that is still going uses a throttle instead: the debounce is re-armed every frame and so never
        // fired until the scroll paused, starving the fringe exactly when it was needed.
        if (hint is { SustainedScroll: true })
        {
            if (System.Threading.Interlocked.Exchange(ref _fringeThrottleArmed, 1) == 0)
            {
                _fringeTimer.Change(FringeThrottleMs, System.Threading.Timeout.Infinite);
            }
        }
        else
        {
            _fringeTimer.Change(FringeDebounceMs, System.Threading.Timeout.Infinite);
        }
    }

    /// <summary>Whether a page should skip the ordinary whole-page request and be left to <see cref="SetStripBandWindow"/> instead (design §4.2) - true only once it's known both to be a strip (<see cref="_stripVerdict"/>) *and* actually bandable (<see cref="_stripBandable"/>, not yet known = assume yes, since it hasn't failed).</summary>
    private bool ShouldRouteAsStrip(int pageIndex)
    {
        lock (_sync)
        {
            return _stripVerdict.TryGetValue(pageIndex, out bool isStrip) && isStrip
                   && !(_stripBandable.TryGetValue(pageIndex, out bool bandable) && !bandable);
        }
    }

    /// <summary>
    /// Disposes a strip's <see cref="StripDecodeSession"/> and evicts its cached bands once the
    /// whole page falls outside the safe/fringe range (design §4.1: "same lifetime shape as a
    /// decoded bitmap leaving <c>_displayCache</c>, just for the session object instead"). Called
    /// under <see cref="_sync"/> by both callers - disposing a session's <see cref="SKCodec"/> here
    /// (not deferred, unlike evicted bitmaps) is safe because a session is a pure decode-time
    /// construct the render thread never touches directly, only the already-separately-queued band
    /// bitmaps it produced.
    /// </summary>
    private void EvictStripSessionsAndBandsOutside(int safeMin, int safeMax)
    {
        List<int>? stalePages = null;
        foreach (var pageIndex in _stripSessions.Keys)
        {
            if (pageIndex < safeMin || pageIndex > safeMax)
            {
                (stalePages ??= new List<int>()).Add(pageIndex);
            }
        }
        if (stalePages is not null)
        {
            foreach (var pageIndex in stalePages)
            {
                var entry = _stripSessions[pageIndex];
                _stripSessions.Remove(pageIndex);
                // Real bug, found via a reproducible native test-host crash: this method runs
                // under _sync alone, but the consumer loop's actual scanline calls (ReadBand) are
                // guarded only by entry.Lock, released between it and _sync being held here - so
                // disposing entry.Session under _sync alone could race a still-in-flight native
                // SKCodec call on the consumer loop thread (use-after-free). entry.Lock is only
                // ever acquired *after* _sync elsewhere in this class (never the other order), so
                // taking it here too can't introduce a lock-ordering deadlock, only (briefly, at
                // most one BandHeight chunk's worth of native work) wait for an in-flight chunk to
                // finish before disposing.
                lock (entry.Lock) { entry.Session.Dispose(); }
            }
        }

        foreach (var key in _bandCache.GetKeys())
        {
            if (key.PageIndex < safeMin || key.PageIndex > safeMax)
            {
                _bandCache.RemoveItem(key);
            }
        }
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

        System.Threading.Interlocked.Exchange(ref _fringeThrottleArmed, 0);
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
            EvictStripSessionsAndBandsOutside(fringeMin, fringeMax);
            PruneMetricState(fringeMin, fringeMax);

            _window.Clear();
            for (int i = fringeMin; i <= fringeMax; i++)
            {
                _window.Add(i);
            }
            _enqueued.RemoveWhere(r => r.PageIndex < fringeMin || r.PageIndex > fringeMax);
        }

        for (int i = fringeMin; i < minIndex; i++)
        {
            if (!ShouldRouteAsStrip(i))
            {
                TryEnqueue(new PipelineRequest(RequestKind.WholePage, i), _lowPriority);
            }
        }
        for (int i = maxIndex + 1; i <= fringeMax; i++)
        {
            if (!ShouldRouteAsStrip(i))
            {
                TryEnqueue(new PipelineRequest(RequestKind.WholePage, i), _lowPriority);
            }
        }

        // Size peeks for the wider fringe too, same low priority as the fringe's own whole-page
        // prefetch - a page this far out doesn't need its real size *now*, just eventually, before
        // it's actually close enough to matter.
        for (int i = fringeMin; i <= fringeMax; i++)
        {
            bool sizeKnown;
            lock (_sync) { sizeKnown = _knownPageSize.ContainsKey(i); }
            if (!sizeKnown)
            {
                TryEnqueue(new PipelineRequest(RequestKind.PageSize, i), _lowPriority);
            }
        }

        OnFringeRecomputed?.Invoke();
    }

    private void TryEnqueue(PipelineRequest request, Channel<PipelineRequest> channel)
    {
        lock (_sync)
        {
            if (_enqueued.Contains(request))
            {
                return;
            }
            switch (request.Kind)
            {
                case RequestKind.WholePage when _displayCache.IsCached(DisplayId(request.PageIndex)):
                case RequestKind.PageSize when _knownPageSize.ContainsKey(request.PageIndex):
                case RequestKind.Band when _bandCache.IsCached(new StripBandId(_container, _containerStamp, request.PageIndex, request.Band)):
                    return;
            }
            _enqueued.Add(request);
        }
        channel.Writer.TryWrite(request);
    }

    /// <summary>
    /// Requests a header-only size peek for <paramref name="pageIndex"/> (design §4.3) -
    /// fire-and-forget, same shape as <see cref="SetVirtualizationWindow"/> itself: enqueues and
    /// returns immediately, never touching the container/decode work synchronously on the calling
    /// thread. A no-op if the size is already known or already enqueued (<see cref="TryEnqueue"/>'s
    /// own dedup). <see cref="PageSizeAvailable"/> fires once the background consumer loop actually
    /// processes it.
    /// </summary>
    public void RequestPageSize(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PageCount)
        {
            return;
        }
        TryEnqueue(new PipelineRequest(RequestKind.PageSize, pageIndex), _highPriority);
    }

    /// <summary>
    /// The sub-page-range analogue of <see cref="SetVirtualizationWindow"/> (design §4.2) - a
    /// strip page can span far more viewport than <see cref="SetVirtualizationWindow"/>'s own
    /// whole-page granularity can express, so this carries which bands of one specific strip are
    /// actually near the viewport. Decodes <paramref name="minBand"/>..<paramref name="maxBand"/>
    /// plus one <see cref="BandHeight"/> margin at high priority, prefetches a further 1-3 bands via
    /// the same adaptive-fringe mechanism whole-page decode already uses (scaled from
    /// <see cref="_forwardFringe"/>'s own 2-6 page range), and evicts bands outside 2 margins. A
    /// no-op if <paramref name="pageIndex"/> isn't currently in <see cref="_window"/> (i.e. this
    /// call raced a <see cref="SetVirtualizationWindow"/> that already moved past it).
    /// </summary>
    public void SetStripBandWindow(int pageIndex, int minBand, int maxBand)
    {
        if (pageIndex < 0 || pageIndex >= PageCount || maxBand < minBand)
        {
            return;
        }

        bool inWindow;
        lock (_sync) { inWindow = _window.Contains(pageIndex); }
        if (!inWindow)
        {
            return;
        }

        int bandFringe = 1 + Math.Clamp((_forwardFringe - MinForwardFringe) / 2, 0, 2); // maps 2..6 -> 1..3

        int decodeMin = Math.Max(0, minBand - 1);
        int decodeMax = maxBand + 1;
        int prefetchMin = Math.Max(0, minBand - 1 - bandFringe);
        int prefetchMax = maxBand + 1 + bandFringe;
        int evictMin = minBand - 2;
        int evictMax = maxBand + 2;

        lock (_sync)
        {
            foreach (var key in _bandCache.GetKeys())
            {
                if (key.PageIndex == pageIndex && (key.Band < evictMin || key.Band > evictMax))
                {
                    _bandCache.RemoveItem(key);
                }
            }
            _enqueued.RemoveWhere(r => r.Kind == RequestKind.Band && r.PageIndex == pageIndex && (r.Band < evictMin || r.Band > evictMax));
        }

        for (int b = decodeMin; b <= decodeMax; b++)
        {
            TryEnqueue(new PipelineRequest(RequestKind.Band, pageIndex, b), _highPriority);
        }
        for (int b = prefetchMin; b < decodeMin; b++)
        {
            TryEnqueue(new PipelineRequest(RequestKind.Band, pageIndex, b), _lowPriority);
        }
        for (int b = decodeMax + 1; b <= prefetchMax; b++)
        {
            TryEnqueue(new PipelineRequest(RequestKind.Band, pageIndex, b), _lowPriority);
        }
    }

    /// <summary>Public-facing wrapper around <see cref="ShouldRouteAsStrip"/> (design §4.2) - same non-blocking, cached-verdict-only check the pipeline uses for its own routing, exposed so <see cref="Paperbunkr.App.Views.PageCanvas"/> can decide whether to build a whole-page or band-slot <see cref="Paperbunkr.App.Views.ContinuousPageEntry"/> for a given page.</summary>
    public bool IsKnownBandableStrip(int pageIndex) => ShouldRouteAsStrip(pageIndex);

    /// <summary>Explicit interface implementation (design §4.1.1) - this class already has an internal const of the same name (<see cref="BandHeight"/>) that every internal caller (<see cref="StripDecodeSession"/>, this class's own tests) uses directly; exposing it on <see cref="IReaderPageSource"/> too needs a differently-bound member, not a renamed constant every existing reference would need updating for.</summary>
    int IReaderPageSource.BandHeight => BandHeight;

    private async Task RunConsumerLoopAsync()
    {
        var token = _cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_highPriority.Reader.TryRead(out var high))
                {
                    ProcessQueuedRequest(high);
                    continue;
                }
                if (_lowPriority.Reader.TryRead(out var low))
                {
                    ProcessQueuedRequest(low);
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

    private void ProcessQueuedRequest(PipelineRequest request)
    {
        switch (request.Kind)
        {
            case RequestKind.WholePage:
                ProcessQueuedPage(request.PageIndex);
                break;
            case RequestKind.PageSize:
                ProcessPageSizeRequest(request.PageIndex);
                break;
            case RequestKind.Band:
                ProcessBandRequest(request.PageIndex, request.Band);
                break;
        }
    }

    /// <summary>
    /// The header-only-peek counterpart to <see cref="ProcessQueuedPage"/> (design §4.3) - runs on
    /// the background consumer loop, never the UI thread, so <see cref="PeekPageSize"/>'s
    /// <see cref="ReadRawBytes"/> call (a real, potentially-blocking container read for anything not
    /// already in <see cref="SharedRawCache"/>) never risks a synchronous UI-thread stall the way a
    /// naive "peek inline as each page enters the window" implementation would have.
    /// </summary>
    private void ProcessPageSizeRequest(int pageIndex)
    {
        lock (_sync)
        {
            _enqueued.Remove(new PipelineRequest(RequestKind.PageSize, pageIndex));
            if (!_window.Contains(pageIndex) || _knownPageSize.ContainsKey(pageIndex))
            {
                return;
            }
        }

        OnBeforePageSizePeek?.Invoke(pageIndex);
        var size = PeekPageSize(pageIndex);
        if (size is not { } resolved)
        {
            return;
        }

        // Free: the size peek already read the header this needs (design §3 - "the same capability
        // §4.3's layout fix needs, built once, used by both") - populating _stripVerdict here means
        // the per-frame window/routing pass (SetVirtualizationWindow) never has to peek itself, only
        // read this already-cached verdict.
        bool isStrip = resolved.Height / (double)resolved.Width > StripAspectThreshold;

        lock (_sync)
        {
            if (!_window.Contains(pageIndex))
            {
                return;
            }
            _knownPageSize[pageIndex] = resolved;
            _stripVerdict[pageIndex] = isStrip;
        }

        try { PageSizeAvailable?.Invoke(pageIndex, resolved); } catch { }
    }

    /// <summary>
    /// Returns the open <see cref="StripDecodeSession"/> for <paramref name="pageIndex"/>, opening
    /// one if none exists yet - or <see langword="null"/> if this page's format doesn't actually
    /// support band decode (design §4.1's rev 5 finding: PNG doesn't) or the bytes couldn't be
    /// read. <see cref="_stripBandable"/> remembers a <see langword="false"/> verdict so a page
    /// that turns out unbandable isn't retried on every subsequent band request for it - one failed
    /// <see cref="StripDecodeSession.TryCreate"/> attempt per page, not one per band.
    /// </summary>
    private StripDecodeSessionEntry? GetOrCreateStripSession(int pageIndex)
    {
        lock (_sync)
        {
            if (_stripSessions.TryGetValue(pageIndex, out var existing))
            {
                return existing;
            }
            if (_stripBandable.TryGetValue(pageIndex, out bool knownBandable) && !knownBandable)
            {
                return null;
            }
        }

        byte[]? bytes = ReadRawBytes(pageIndex);
        if (bytes is not { Length: > 0 })
        {
            lock (_sync) { _stripBandable[pageIndex] = false; }
            return null;
        }

        // A fresh lock scoped to this one page's session (design rev 5) - not the pipeline-wide
        // _readerLock, which would let an unrelated page's real container read contend with this
        // strip's Skia decode for no reason.
        var sessionLock = new object();
        var session = StripDecodeSession.TryCreate(bytes, action => { lock (sessionLock) { action(); } });

        if (session is null)
        {
            lock (_sync) { _stripBandable[pageIndex] = false; }
            return null;
        }

        var entry = new StripDecodeSessionEntry(session);
        lock (_sync)
        {
            // Lost a race with another thread that already opened one (a UI-thread cold-miss
            // against the background loop, both wanting this same strip's first band) - keep
            // whichever landed first, discard this one.
            if (_stripSessions.TryGetValue(pageIndex, out var already))
            {
                session.Dispose();
                return already;
            }
            _stripSessions[pageIndex] = entry;
            _stripBandable[pageIndex] = true;
            return entry;
        }
    }

    /// <summary>
    /// Decodes one band of a strip page (design §4.1) via its open <see cref="StripDecodeSession"/>,
    /// opening or reopening one as needed. Falls back to whole-page decode
    /// (<see cref="ProcessQueuedPage"/>) whenever banding turns out not to work for this page - not
    /// bandable at all (<see cref="GetOrCreateStripSession"/> returned <see langword="null"/>), or a
    /// backward/far-forward jump that a fresh reopened session also failed on - so a PNG strip (or
    /// any other decode failure) still ends up with *something* on screen rather than a permanent
    /// gap.
    /// </summary>
    private void ProcessBandRequest(int pageIndex, int band)
    {
        var bandId = new StripBandId(_container, _containerStamp, pageIndex, band);
        lock (_sync)
        {
            _enqueued.Remove(new PipelineRequest(RequestKind.Band, pageIndex, band));
            if (!_window.Contains(pageIndex) || _bandCache.IsCached(bandId))
            {
                return;
            }
        }

        DrainPendingDispose();
        OnBeforeBackgroundDecode?.Invoke(pageIndex);
        Stats.RecordBackgroundDecode();

        var entry = GetOrCreateStripSession(pageIndex);
        if (entry is null)
        {
            ProcessQueuedPage(pageIndex);
            return;
        }

        int bandStart = band * BandHeight;

        bool needsReopen;
        lock (entry.Lock) { needsReopen = bandStart < entry.Session.NextUnreadRow; }

        if (needsReopen)
        {
            // Backward or far-forward jump (design §4.1) - this session has already passed the
            // requested row and scanline decode can't seek backward. Reopen fresh, skipping from
            // row 0 - bounded, non-pathological cost, same order of magnitude as this strip's very
            // first band.
            lock (_sync) { _stripSessions.Remove(pageIndex); }
            lock (entry.Lock) { entry.Session.Dispose(); } // consistent invariant: every Session.Dispose() call in this class goes through its own entry.Lock, never unlocked
            entry = GetOrCreateStripSession(pageIndex);
            if (entry is null)
            {
                ProcessQueuedPage(pageIndex);
                return;
            }
        }

        var currentEntry = entry;
        SKBitmap? skBand;
        try
        {
            skBand = currentEntry.Session.ReadBand(bandStart, BandHeight, action => { lock (currentEntry.Lock) { action(); } });
        }
        catch (InvalidOperationException)
        {
            // Forward-only contract violated by a race this method's own reopen check didn't quite
            // catch (two threads both deciding to reopen concurrently) - fall back rather than let
            // the exception take down the consumer loop; a later request will retry cleanly.
            skBand = null;
        }

        if (skBand is null)
        {
            return;
        }

        AvaloniaBitmap converted;
        try
        {
            converted = SkiaBitmapConverter.FromSkBitmap(skBand);
        }
        finally
        {
            skBand.Dispose();
        }

        var decoded = new ReaderBitmap(converted);

        bool landed;
        lock (_sync)
        {
            if (!_window.Contains(pageIndex))
            {
                decoded.Dispose();
                return;
            }
            var stored = _bandCache.LockItem(bandId, _ => decoded);
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

    private void ProcessQueuedPage(int pageIndex)
    {
        lock (_sync)
        {
            _enqueued.Remove(new PipelineRequest(RequestKind.WholePage, pageIndex));
            if (!_window.Contains(pageIndex) || _displayCache.IsCached(DisplayId(pageIndex)))
            {
                return;
            }
        }

        DrainPendingDispose();
        OnBeforeBackgroundDecode?.Invoke(pageIndex);
        Stats.RecordBackgroundDecode();

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

            if (landed && _windowEntryTimestamps.Remove(pageIndex, out long enteredAt))
            {
                Stats.RecordDecodeLatencyMs(Stopwatch.GetElapsedTime(enteredAt).TotalMilliseconds);
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
            _bandCache.Dispose();
            // SharedRawCache is process-wide (§9 back-nav retention) - not disposed here.

            foreach (var entry in _stripSessions.Values)
            {
                // _consumerLoop.Wait above should mean nothing else touches these anymore, but the
                // same entry.Lock invariant applies here too (cheap insurance if that wait ever
                // times out under real load rather than genuinely observing the loop stopped).
                lock (entry.Lock) { entry.Session.Dispose(); }
            }
            _stripSessions.Clear();
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
