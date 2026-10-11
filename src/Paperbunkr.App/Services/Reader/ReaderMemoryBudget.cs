using System;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// The one adaptive byte budget for a reading session (docs/superpowers/specs/2026-09-08-reader-
/// decode-cache-prefetch-pipeline-design.md §5). Auto = <c>clamp(physical RAM / 32, 128 MiB,
/// 512 MiB)</c>; an explicit user limit (Preferences → Reader) overrides. It was 25% of RAM with the same
/// clamp, which is the 512 MiB ceiling on anything with 2 GB or more: on a 7.8 GB machine the reader's
/// pages alone were then a quarter of the app's measured 2.1 GB (2026-10-07). RAM / 32 is about 250 MiB
/// there (ten ordinary 1988x3056 pages) and still the full 512 MiB from 16 GB up. One budget, shared -
/// only one reader (comic / PDF / book) is open at a time - split three ways: decoded display
/// bitmaps, the thumbnail rail, and the compressed-bytes tier.
/// </summary>
public sealed class ReaderMemoryBudget
{
    private const long Mib = 1024 * 1024;
    private const long AutoFloor = 128 * Mib;
    private const long AutoCeiling = 512 * Mib;

    private ReaderMemoryBudget(long totalBytes)
    {
        TotalBytes = totalBytes;
        ThumbnailBytes = Math.Min(32 * Mib, totalBytes / 8);
        RawBytesBytes = Math.Min(64 * Mib, totalBytes / 4);
        DisplayBytes = totalBytes - ThumbnailBytes;
    }

    /// <summary>Whole session budget in bytes.</summary>
    public long TotalBytes { get; }

    /// <summary><see cref="Cache{K,T}.SizeCapacity"/> for the decoded display-tier cache.</summary>
    public long DisplayBytes { get; }

    /// <summary><see cref="Cache{K,T}.SizeCapacity"/> for the thumbnail sub-cache.</summary>
    public long ThumbnailBytes { get; }

    /// <summary><see cref="Cache{K,T}.SizeCapacity"/> for the compressed-page-bytes tier (§4/§6.2).</summary>
    public long RawBytesBytes { get; }

    /// <param name="userLimitMb">The Preferences → Reader override; <see langword="null"/> = Auto.</param>
    public static ReaderMemoryBudget Resolve(int? userLimitMb)
    {
        long bytes = userLimitMb is int mb && mb > 0
            ? mb * Mib
            : AutoFor(PhysicalRamBytes());
        return new ReaderMemoryBudget(bytes);
    }

    /// <summary>The Auto budget for a machine with <paramref name="physicalRamBytes"/> of RAM.</summary>
    public static long AutoFor(long physicalRamBytes) => Math.Clamp(physicalRamBytes / 32, AutoFloor, AutoCeiling);

    private static long PhysicalRamBytes()
    {
        try
        {
            long total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return total > 0 ? total : 8L * 1024 * Mib; // fall back to an 8 GB assumption
        }
        catch
        {
            return 8L * 1024 * Mib;
        }
    }
}
