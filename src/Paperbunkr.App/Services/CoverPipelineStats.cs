using System.Threading;

namespace Paperbunkr.App.Services;

/// <summary>
/// Cheap process-wide counters for the Library scroll/cover pipeline (docs/superpowers/specs/
/// 2026-09-19-library-scroll-smoothness-design.md, "Goals and how they are measured"). Plain
/// <see cref="Interlocked"/> increments, always on: one uncontended atomic add per event is far
/// below anything measurable next to a decode or a layout pass. Read by the dev-only
/// <c>Paperbunkr.ScrollHarness</c> and by tests; nothing in the app's behavior depends on them.
/// </summary>
internal static class CoverPipelineStats
{
    private static long s_cacheHits;
    private static long s_cacheMisses;
    private static long s_decodesStarted;
    private static long s_decodesApplied;
    private static long s_decodesWasted;
    private static long s_wastedStale;
    private static long s_wastedEmpty;
    private static long s_gridRequests;
    private static long s_decodeMicros;
    private static long s_queueWaitMicros;
    private static long s_timedDecodes;
    private static long s_entranceTimersArmed;
    private static long s_panelMeasurePasses;
    private static long s_panelRealizeChanges;

    /// <summary>Harness-only: artificial per-decode delay in milliseconds (a slow disk or a cold cache), applied by both the legacy and the grid decoder. 0 in the app.</summary>
    public static int SimulatedDecodeDelayMs { get; set; }

    /// <summary>Harness-only: send every cover through the original full-size shared-cache path, to measure it against the grid pipeline. False in the app.</summary>
    public static bool ForceLegacyCoverPath { get; set; }

    public static long CacheHits => Interlocked.Read(ref s_cacheHits);

    /// <summary>Requests that found nothing in memory and had to go to the decode path.</summary>
    public static long CacheMisses => Interlocked.Read(ref s_cacheMisses);

    /// <summary>Decodes that actually began (deduplicated per cover).</summary>
    public static long DecodesStarted => Interlocked.Read(ref s_decodesStarted);

    /// <summary>Finished decodes painted onto a live <c>Image</c>.</summary>
    public static long DecodesApplied => Interlocked.Read(ref s_decodesApplied);

    /// <summary>Finished decodes whose container had already been recycled (or that came back empty) - work nobody saw.</summary>
    public static long DecodesWasted => Interlocked.Read(ref s_decodesWasted);

    /// <summary>Of <see cref="DecodesWasted"/>: the container had been recycled to another cover (generation mismatch).</summary>
    public static long WastedStale => Interlocked.Read(ref s_wastedStale);

    /// <summary>Of <see cref="DecodesWasted"/>: the decode came back empty (missing file, dropped from the queue).</summary>
    public static long WastedEmpty => Interlocked.Read(ref s_wastedEmpty);

    /// <summary>Requests handed to the grid decode queue.</summary>
    public static long GridRequests => Interlocked.Read(ref s_gridRequests);

    /// <summary>Total decode time, in microseconds, over <see cref="TimedDecodes"/> decodes.</summary>
    public static long DecodeMicros => Interlocked.Read(ref s_decodeMicros);

    /// <summary>Total time requests waited in the queue before a worker picked them up, in microseconds.</summary>
    public static long QueueWaitMicros => Interlocked.Read(ref s_queueWaitMicros);

    public static long TimedDecodes => Interlocked.Read(ref s_timedDecodes);

    /// <summary>Entrance-animation <c>DispatcherTimer</c>s started (scroll recycling should not arm any).</summary>
    public static long EntranceTimersArmed => Interlocked.Read(ref s_entranceTimersArmed);

    /// <summary>Times the wrap panel ran <c>MeasureOverride</c>.</summary>
    public static long PanelMeasurePasses => Interlocked.Read(ref s_panelMeasurePasses);

    /// <summary>Times the wrap panel's realized row range actually changed (containers realized or recycled).</summary>
    public static long PanelRealizeChanges => Interlocked.Read(ref s_panelRealizeChanges);

    public static void CacheHit() => Interlocked.Increment(ref s_cacheHits);

    public static void CacheMiss() => Interlocked.Increment(ref s_cacheMisses);

    public static void DecodeStarted() => Interlocked.Increment(ref s_decodesStarted);

    public static void DecodeApplied() => Interlocked.Increment(ref s_decodesApplied);

    public static void DecodeWasted() => Interlocked.Increment(ref s_decodesWasted);

    public static void DecodeTimed(long decodeMicros, long queueWaitMicros)
    {
        Interlocked.Add(ref s_decodeMicros, decodeMicros);
        Interlocked.Add(ref s_queueWaitMicros, queueWaitMicros);
        Interlocked.Increment(ref s_timedDecodes);
    }

    public static void WastedBecauseStale() => Interlocked.Increment(ref s_wastedStale);

    public static void WastedBecauseEmpty() => Interlocked.Increment(ref s_wastedEmpty);

    public static void GridRequested() => Interlocked.Increment(ref s_gridRequests);

    public static void EntranceTimerArmed() => Interlocked.Increment(ref s_entranceTimersArmed);

    public static void PanelMeasured() => Interlocked.Increment(ref s_panelMeasurePasses);

    public static void PanelRealizeChanged() => Interlocked.Increment(ref s_panelRealizeChanges);

    /// <summary>Zeroes every counter (harness/tests, between scenarios).</summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref s_cacheHits, 0);
        Interlocked.Exchange(ref s_cacheMisses, 0);
        Interlocked.Exchange(ref s_decodesStarted, 0);
        Interlocked.Exchange(ref s_decodesApplied, 0);
        Interlocked.Exchange(ref s_decodesWasted, 0);
        Interlocked.Exchange(ref s_wastedStale, 0);
        Interlocked.Exchange(ref s_wastedEmpty, 0);
        Interlocked.Exchange(ref s_gridRequests, 0);
        Interlocked.Exchange(ref s_decodeMicros, 0);
        Interlocked.Exchange(ref s_queueWaitMicros, 0);
        Interlocked.Exchange(ref s_timedDecodes, 0);
        Interlocked.Exchange(ref s_entranceTimersArmed, 0);
        Interlocked.Exchange(ref s_panelMeasurePasses, 0);
        Interlocked.Exchange(ref s_panelRealizeChanges, 0);
    }
}
