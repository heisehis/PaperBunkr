using System;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// The one adaptive byte budget for a reading session (docs/superpowers/specs/2026-09-08-reader-
/// decode-cache-prefetch-pipeline-design.md §5). Auto = <c>clamp(25% physical RAM, 128 MiB,
/// 512 MiB)</c>; an explicit user limit (Preferences → Reader) overrides. One budget, shared -
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
            : Math.Clamp(PhysicalRamBytes() / 4, AutoFloor, AutoCeiling);
        return new ReaderMemoryBudget(bytes);
    }

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
