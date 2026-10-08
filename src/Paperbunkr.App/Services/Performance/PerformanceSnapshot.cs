using System;
using System.Diagnostics;
using System.Text;

namespace Paperbunkr.App.Services.Performance;

/// <summary>
/// One reading of where the process's memory is (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md §4.1). The
/// three memory figures are kept apart because they answer different questions:
/// <list type="bullet">
///   <item><see cref="ManagedBytes"/>: what the .NET heap holds.</item>
///   <item><see cref="PrivateBytes"/>: memory committed to this process alone, the real footprint. Native buffers (decoded
///   bitmaps, the panel-detection model) show up here and not in the managed figure.</item>
///   <item><see cref="WorkingSetBytes"/>: what Task Manager shows. It also counts file-backed pages Windows can take back, so it
///   can be high without anything being wrong.</item>
/// </list>
/// <see cref="NativeEstimateBytes"/> (private minus managed) is what tells a managed leak from native growth.
/// </summary>
public sealed record PerformanceSnapshot(
    long ManagedBytes,
    long PrivateBytes,
    long WorkingSetBytes,
    long GcHeapBytes,
    long GcFragmentedBytes,
    int Gen2Collections,
    int SystemLoadPercent,
    long SystemTotalBytes,
    long ImageBudgetBytes,
    long GridCoverBytes,
    int GridCoverCount,
    long ComicCoverBytes,
    int ComicCoverCount,
    long BookCoverBytes,
    int BookCoverCount,
    int Threads,
    bool HeavyJobRunning,
    int HeavyJobsWaiting,
    int PressureTrims)
{
    public long NativeEstimateBytes => Math.Max(0, PrivateBytes - ManagedBytes);

    public static PerformanceSnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();
        return new PerformanceSnapshot(
            ManagedBytes: GC.GetTotalMemory(forceFullCollection: false),
            PrivateBytes: process.PrivateMemorySize64,
            WorkingSetBytes: Environment.WorkingSet,
            GcHeapBytes: gc.HeapSizeBytes,
            GcFragmentedBytes: gc.FragmentedBytes,
            Gen2Collections: GC.CollectionCount(2),
            SystemLoadPercent: SystemMemory.LoadPercent,
            SystemTotalBytes: SystemMemory.TotalPhysicalBytes,
            ImageBudgetBytes: ImageMemoryBudget.TotalBytes,
            GridCoverBytes: GridCoverCache.Shared.Bytes,
            GridCoverCount: GridCoverCache.Shared.Count,
            ComicCoverBytes: CoverImageCache.CachedBytes,
            ComicCoverCount: CoverImageCache.CachedCount,
            BookCoverBytes: BookCoverImageCache.CachedBytes,
            BookCoverCount: BookCoverImageCache.CachedCount,
            Threads: process.Threads.Count,
            HeavyJobRunning: HeavyJobLane.Shared.IsBusy,
            HeavyJobsWaiting: HeavyJobLane.Shared.WaitingCount,
            PressureTrims: MemoryPressureTrimmer.Shared.TrimCount);
    }

    /// <summary>The snapshot as aligned "label : value" lines, the same shape as the crash report's program-info block.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Private      : {Mb(PrivateBytes)} MB");
        sb.AppendLine($"Managed heap : {Mb(ManagedBytes)} MB (GC heap {Mb(GcHeapBytes)} MB, fragmented {Mb(GcFragmentedBytes)} MB, gen-2 collections {Gen2Collections})");
        sb.AppendLine($"Native (est.): {Mb(NativeEstimateBytes)} MB");
        sb.AppendLine($"System memory: {SystemLoadPercent}% of {Mb(SystemTotalBytes)} MB in use");
        sb.AppendLine($"Cover caches : grid {Mb(GridCoverBytes)} MB ({GridCoverCount}), comics {Mb(ComicCoverBytes)} MB ({ComicCoverCount}), books {Mb(BookCoverBytes)} MB ({BookCoverCount}); budget {Mb(ImageBudgetBytes)} MB; pressure trims {PressureTrims}");
        sb.AppendLine($"Threads      : {Threads}");
        sb.AppendLine($"Heavy jobs   : {(HeavyJobRunning ? "1 running" : "none running")}, {HeavyJobsWaiting} waiting");
        return sb.ToString();
    }

    private static long Mb(long bytes) => bytes / 1024 / 1024;
}
