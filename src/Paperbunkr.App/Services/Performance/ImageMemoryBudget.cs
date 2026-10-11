using System;

namespace Paperbunkr.App.Services.Performance;

/// <summary>
/// How much memory the decoded-cover caches may hold between them (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md
/// §4.2): 5% of installed RAM, never under 150 MB (a 4 GB machine still needs a screenful of covers) and never over 500 MB (more
/// buys nothing on a large machine). Replaces a fixed 300 MB grid budget plus two caches of up to 5,000 full thumbnails each.
/// </summary>
public static class ImageMemoryBudget
{
    public const long MinBytes = 150L * 1024 * 1024;
    public const long MaxBytes = 500L * 1024 * 1024;
    public const double ShareOfRam = 0.05;

    /// <summary>The Library grid's display-size covers take most of it; the full-thumbnail caches behind the other screens share the rest.</summary>
    public const double GridShare = 0.60;
    public const double ComicShare = 0.30;
    public const double BookShare = 0.10;

    public static long TotalFor(long physicalBytes) =>
        Math.Clamp((long)(Math.Max(0, physicalBytes) * ShareOfRam), MinBytes, MaxBytes);

    private static readonly Lazy<long> s_total = new(() => TotalFor(SystemMemory.TotalPhysicalBytes));

    public static long TotalBytes => s_total.Value;

    public static long GridBytes => (long)(TotalBytes * GridShare);

    public static long ComicBytes => (long)(TotalBytes * ComicShare);

    public static long BookBytes => (long)(TotalBytes * BookShare);

    public const long GpuCacheMinBytes = 96L * 1024 * 1024;
    public const long GpuCacheMaxBytes = 384L * 1024 * 1024;

    /// <summary>
    /// Skia's GPU resource cache limit: 2.5% of RAM, between 96 MB and the 384 MB the reader spec chose for desktop machines
    /// (so from 16 GB up nothing changes). On integrated graphics that cache is ordinary system memory charged to the process,
    /// and a fixed 384 MB was a large share of a 7.8 GB machine's footprint (2026-10-07: about 380 MB of 16 MB blocks, which
    /// tracked this limit). About 200 MB there.
    /// </summary>
    public static long GpuCacheFor(long physicalBytes) =>
        Math.Clamp((long)(Math.Max(0, physicalBytes) * 0.025), GpuCacheMinBytes, GpuCacheMaxBytes);

    public static long GpuCacheBytes => GpuCacheFor(SystemMemory.TotalPhysicalBytes);
}
