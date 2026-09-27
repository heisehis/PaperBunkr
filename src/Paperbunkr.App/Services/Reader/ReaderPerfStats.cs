using System;
using System.Linq;
using System.Threading;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// Lightweight, always-on perf counters for the reader (docs/superpowers/specs/2026-09-08-reader-
/// decode-cache-prefetch-pipeline-design.md §10 - the data behind the <c>ReaderFrameStats</c>
/// overlay; the overlay's own XAML is a later, GUI-verified slice). Process-wide singleton so the
/// pipeline (decode/cache side) and the compositor visual handler (frame-time side) both feed one
/// view. Cheap: a ring buffer of doubles plus interlocked counters. The brief's "&lt; 1 ms render"
/// target is <b>observed</b> here (p50/p99 of <see cref="RecordComposeFrameMs"/>), never asserted
/// in CI.
///
/// Continuous-scroll boundary metrics (docs/superpowers/specs/2026-09-25-comic-reader-performance-
/// design.md B5) - boundary-handler time, decode latency, blank-page frames, layout shift and
/// position-save time - exist so a stall at a page boundary can be measured on real content instead
/// of guessed at.
/// </summary>
public sealed class ReaderPerfStats
{
    public static ReaderPerfStats Current { get; } = new();

    private volatile string? _lastInput;

    /// <summary>The last reader input that arrived from a device that is hard to diagnose (media key, mouse side button, gamepad), for the overlay's "last input" line (reach design, testing).</summary>
    public string? LastInput => _lastInput;

    public void RecordInput(string description) => _lastInput = $"{description} @ {DateTime.Now:HH:mm:ss}";

    /// <summary>The overlay text: the snapshot plus, when a device input was seen, the last one.</summary>
    public string OverlayText() =>
        _lastInput is { } last ? $"{Snapshot()}{Environment.NewLine}last input: {last}" : Snapshot().ToString();

    private const int Window = 240; // ~4 s at 60 fps
    private const int MetricWindow = 120;

    private readonly double[] _frameMs = new double[Window];
    private int _frameCount;
    private readonly object _frameLock = new();

    private readonly MetricRing _boundaryMs = new(MetricWindow);
    private readonly MetricRing _decodeLatencyMs = new(MetricWindow);
    private readonly MetricRing _positionSaveMs = new(MetricWindow);

    private long _cacheHits;
    private long _cacheMisses;
    private long _backgroundDecodes;
    private long _synchronousDecodes;
    private long _archiveReads;
    private long _sessionReads;

    private long _framesWithBlank;
    private long _worstBlankPages;
    private long _layoutShiftEvents;
    private readonly object _shiftLock = new();
    private double _maxLayoutShiftPx;

    public void RecordComposeFrameMs(double ms)
    {
        lock (_frameLock)
        {
            _frameMs[_frameCount % Window] = ms;
            _frameCount++;
        }
    }

    public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);
    public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);
    public void RecordBackgroundDecode() => Interlocked.Increment(ref _backgroundDecodes);
    public void RecordSynchronousDecode() => Interlocked.Increment(ref _synchronousDecodes);
    public void RecordArchiveRead() => Interlocked.Increment(ref _archiveReads);
    public void RecordSessionRead() => Interlocked.Increment(ref _sessionReads);

    /// <summary>How long the view model's continuous "current page changed" handler took (it runs synchronously inside the frame callback).</summary>
    public void RecordBoundaryHandlerMs(double ms) => _boundaryMs.Add(ms);

    /// <summary>Time from a page entering the pipeline's window to its decode landing in the cache.</summary>
    public void RecordDecodeLatencyMs(double ms) => _decodeLatencyMs.Add(ms);

    /// <summary>How long the debounced reading-position save took (now off the UI thread, still worth watching).</summary>
    public void RecordPositionSaveMs(double ms) => _positionSaveMs.Add(ms);

    /// <summary>One continuous-mode frame's count of on-screen pages that had no decoded bitmap yet (drawn as a gap). Only frames with a gap are counted.</summary>
    public void RecordBlankPages(int visibleBlankPages)
    {
        if (visibleBlankPages <= 0)
        {
            return;
        }

        Interlocked.Increment(ref _framesWithBlank);
        long current;
        do
        {
            current = Interlocked.Read(ref _worstBlankPages);
            if (visibleBlankPages <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _worstBlankPages, visibleBlankPages, current) != current);
    }

    /// <summary>A page above the viewport learned its real size and the visible content moved (or would have) by <paramref name="pixels"/>.</summary>
    public void RecordLayoutShift(double pixels)
    {
        double magnitude = Math.Abs(pixels);
        if (magnitude <= 0)
        {
            return;
        }

        Interlocked.Increment(ref _layoutShiftEvents);
        lock (_shiftLock)
        {
            if (magnitude > _maxLayoutShiftPx)
            {
                _maxLayoutShiftPx = magnitude;
            }
        }
    }

    public ReaderPerfSnapshot Snapshot()
    {
        double p50, p99;
        int frames;
        lock (_frameLock)
        {
            frames = Math.Min(_frameCount, Window);
            if (frames == 0)
            {
                p50 = p99 = 0;
            }
            else
            {
                var sorted = _frameMs.Take(frames).OrderBy(x => x).ToArray();
                p50 = sorted[(int)(frames * 0.50)];
                p99 = sorted[Math.Min(frames - 1, (int)(frames * 0.99))];
            }
        }

        long hits = Interlocked.Read(ref _cacheHits);
        long misses = Interlocked.Read(ref _cacheMisses);
        double hitRatio = hits + misses == 0 ? 0 : (double)hits / (hits + misses);

        double maxShift;
        lock (_shiftLock) { maxShift = _maxLayoutShiftPx; }

        var (boundaryP50, boundaryP99, boundaryCount) = _boundaryMs.Percentiles();
        var (latencyP50, latencyP99, _) = _decodeLatencyMs.Percentiles();
        var (_, saveP99, _) = _positionSaveMs.Percentiles();

        return new ReaderPerfSnapshot(
            FrameP50Ms: p50,
            FrameP99Ms: p99,
            FramesSampled: frames,
            CacheHitRatio: hitRatio,
            CacheHits: hits,
            CacheMisses: misses,
            BackgroundDecodes: Interlocked.Read(ref _backgroundDecodes),
            SynchronousDecodes: Interlocked.Read(ref _synchronousDecodes),
            ArchiveReads: Interlocked.Read(ref _archiveReads),
            SessionReads: Interlocked.Read(ref _sessionReads))
        {
            BoundaryP50Ms = boundaryP50,
            BoundaryP99Ms = boundaryP99,
            BoundaryEvents = boundaryCount,
            DecodeLatencyP50Ms = latencyP50,
            DecodeLatencyP99Ms = latencyP99,
            FramesWithBlankPage = Interlocked.Read(ref _framesWithBlank),
            WorstBlankPages = Interlocked.Read(ref _worstBlankPages),
            LayoutShiftEvents = Interlocked.Read(ref _layoutShiftEvents),
            MaxLayoutShiftPx = maxShift,
            PositionSaveP99Ms = saveP99,
        };
    }

    /// <summary>Zeroes everything - called when a new book opens so the numbers describe the current session.</summary>
    public void Reset()
    {
        lock (_frameLock)
        {
            Array.Clear(_frameMs);
            _frameCount = 0;
        }
        _boundaryMs.Clear();
        _decodeLatencyMs.Clear();
        _positionSaveMs.Clear();
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _backgroundDecodes, 0);
        Interlocked.Exchange(ref _synchronousDecodes, 0);
        Interlocked.Exchange(ref _archiveReads, 0);
        Interlocked.Exchange(ref _sessionReads, 0);
        Interlocked.Exchange(ref _framesWithBlank, 0);
        Interlocked.Exchange(ref _worstBlankPages, 0);
        Interlocked.Exchange(ref _layoutShiftEvents, 0);
        lock (_shiftLock) { _maxLayoutShiftPx = 0; }
    }

    /// <summary>A small thread-safe ring of samples with p50/p99.</summary>
    private sealed class MetricRing(int size)
    {
        private readonly double[] _samples = new double[size];
        private int _count;
        private readonly object _lock = new();

        public void Add(double value)
        {
            lock (_lock)
            {
                _samples[_count % _samples.Length] = value;
                _count++;
            }
        }

        public (double P50, double P99, long Total) Percentiles()
        {
            lock (_lock)
            {
                int n = Math.Min(_count, _samples.Length);
                if (n == 0)
                {
                    return (0, 0, _count);
                }

                var sorted = _samples.Take(n).OrderBy(x => x).ToArray();
                return (sorted[(int)(n * 0.50)], sorted[Math.Min(n - 1, (int)(n * 0.99))], _count);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_samples);
                _count = 0;
            }
        }
    }
}

public readonly record struct ReaderPerfSnapshot(
    double FrameP50Ms,
    double FrameP99Ms,
    int FramesSampled,
    double CacheHitRatio,
    long CacheHits,
    long CacheMisses,
    long BackgroundDecodes,
    long SynchronousDecodes,
    long ArchiveReads,
    long SessionReads)
{
    /// <summary>Continuous-scroll boundary metrics (design 2026-09-25 B5); all zero until something records them.</summary>
    public double BoundaryP50Ms { get; init; }
    public double BoundaryP99Ms { get; init; }
    public long BoundaryEvents { get; init; }
    public double DecodeLatencyP50Ms { get; init; }
    public double DecodeLatencyP99Ms { get; init; }
    public long FramesWithBlankPage { get; init; }
    public long WorstBlankPages { get; init; }
    public long LayoutShiftEvents { get; init; }
    public double MaxLayoutShiftPx { get; init; }
    public double PositionSaveP99Ms { get; init; }

    public override string ToString() =>
        $"frame p50 {FrameP50Ms:F2}ms / p99 {FrameP99Ms:F2}ms ({FramesSampled}) · " +
        $"cache {CacheHitRatio:P0} ({CacheHits}/{CacheHits + CacheMisses}) · " +
        $"decode bg {BackgroundDecodes} sync {SynchronousDecodes} · " +
        $"read session {SessionReads} archive {ArchiveReads}" +
        (HasBoundaryMetrics
            ? $"\nboundary p50 {BoundaryP50Ms:F2}ms / p99 {BoundaryP99Ms:F2}ms ({BoundaryEvents}) · " +
              $"decode latency p50 {DecodeLatencyP50Ms:F0}ms / p99 {DecodeLatencyP99Ms:F0}ms · " +
              $"blank frames {FramesWithBlankPage} (worst {WorstBlankPages}) · " +
              $"shift {LayoutShiftEvents}x max {MaxLayoutShiftPx:F0}px · save p99 {PositionSaveP99Ms:F1}ms"
            : string.Empty);

    private bool HasBoundaryMetrics =>
        BoundaryEvents > 0 || FramesWithBlankPage > 0 || LayoutShiftEvents > 0 || DecodeLatencyP99Ms > 0 || PositionSaveP99Ms > 0;
}
