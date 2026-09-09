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
/// </summary>
public sealed class ReaderPerfStats
{
    public static ReaderPerfStats Current { get; } = new();

    private const int Window = 240; // ~4 s at 60 fps

    private readonly double[] _frameMs = new double[Window];
    private int _frameCount;
    private readonly object _frameLock = new();

    private long _cacheHits;
    private long _cacheMisses;
    private long _backgroundDecodes;
    private long _synchronousDecodes;
    private long _archiveReads;
    private long _sessionReads;

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
            SessionReads: Interlocked.Read(ref _sessionReads));
    }

    /// <summary>Zeroes everything - called when a new book opens so the numbers describe the current session.</summary>
    public void Reset()
    {
        lock (_frameLock)
        {
            Array.Clear(_frameMs);
            _frameCount = 0;
        }
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _backgroundDecodes, 0);
        Interlocked.Exchange(ref _synchronousDecodes, 0);
        Interlocked.Exchange(ref _archiveReads, 0);
        Interlocked.Exchange(ref _sessionReads, 0);
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
    public override string ToString() =>
        $"frame p50 {FrameP50Ms:F2}ms / p99 {FrameP99Ms:F2}ms ({FramesSampled}) · " +
        $"cache {CacheHitRatio:P0} ({CacheHits}/{CacheHits + CacheMisses}) · " +
        $"decode bg {BackgroundDecodes} sync {SynchronousDecodes} · " +
        $"read session {SessionReads} archive {ArchiveReads}";
}
