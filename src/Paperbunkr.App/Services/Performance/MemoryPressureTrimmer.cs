using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Paperbunkr.App.Services.Performance;

/// <summary>
/// Shrinks the cover caches when the whole system is short of memory (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md
/// §4.2). There is no timer: <see cref="Check"/> is called from the cache add paths, which is exactly when the app is taking more
/// memory, and it does nothing more often than every <see cref="CheckInterval"/>.
///
/// Two readings in a row at or above <see cref="ThresholdPercent"/> start a trim on a pool thread, never on the caller's: the caches
/// drop to half their budget. It does not force a blocking collection. If the process's private memory did not fall, one
/// non-blocking background collection is requested, at most once every <see cref="CollectInterval"/>.
/// </summary>
public sealed class MemoryPressureTrimmer
{
    public const int ThresholdPercent = 85;
    public const double TrimToFraction = 0.5;

    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan TrimInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan CollectInterval = TimeSpan.FromMinutes(2);

    public static MemoryPressureTrimmer Shared { get; } = new(
        () => SystemMemory.LoadPercent,
        () => Stopwatch.GetElapsedTime(0),
        TrimCoverCaches,
        ReadPrivateBytes,
        () => GC.Collect(2, GCCollectionMode.Optimized, blocking: false),
        work => Task.Run(work));

    private readonly Func<int> _loadPercent;
    private readonly Func<TimeSpan> _now;
    private readonly Action _trim;
    private readonly Func<long> _privateBytes;
    private readonly Action _requestCollection;
    private readonly Action<Action> _runInBackground;

    private readonly object _gate = new();
    private TimeSpan? _lastCheck;
    private TimeSpan? _lastTrim;
    private TimeSpan? _lastCollect;
    private int _consecutiveHigh;
    private int _trimming;

    public MemoryPressureTrimmer(
        Func<int> loadPercent,
        Func<TimeSpan> now,
        Action trim,
        Func<long> privateBytes,
        Action requestCollection,
        Action<Action> runInBackground)
    {
        _loadPercent = loadPercent;
        _now = now;
        _trim = trim;
        _privateBytes = privateBytes;
        _requestCollection = requestCollection;
        _runInBackground = runInBackground;
    }

    /// <summary>Trims started so far (diagnostics and tests).</summary>
    public int TrimCount { get; private set; }

    /// <summary>Cheap enough to call on every cache add: usually one timestamp comparison under a lock.</summary>
    public void Check()
    {
        lock (_gate)
        {
            var now = _now();
            if (_lastCheck is { } last && now - last < CheckInterval)
            {
                return;
            }

            _lastCheck = now;
            _consecutiveHigh = _loadPercent() >= ThresholdPercent ? _consecutiveHigh + 1 : 0;
            if (_consecutiveHigh < 2 || (_lastTrim is { } lastTrim && now - lastTrim < TrimInterval))
            {
                return;
            }

            _lastTrim = now;
            _consecutiveHigh = 0;
            TrimCount++;
        }

        if (Interlocked.Exchange(ref _trimming, 1) == 1)
        {
            return;
        }

        _runInBackground(() =>
        {
            try
            {
                long before = _privateBytes();
                _trim();
                if (_privateBytes() >= before && CollectIsDue())
                {
                    _requestCollection();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _trimming, 0);
            }
        });
    }

    private bool CollectIsDue()
    {
        lock (_gate)
        {
            var now = _now();
            if (_lastCollect is { } last && now - last < CollectInterval)
            {
                return false;
            }

            _lastCollect = now;
            return true;
        }
    }

    private static void TrimCoverCaches()
    {
        GridCoverCache.Shared.Trim((long)(GridCoverCache.BudgetBytes * TrimToFraction));
        CoverImageCache.Trim(TrimToFraction);
        BookCoverImageCache.Trim(TrimToFraction);
    }

    private static long ReadPrivateBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }
}
