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
        BandBytes = Math.Min(DisplayBytes, Math.Max(DisplayBytes / 4, BandFloor));
    }

    /// <summary>Whole session budget in bytes.</summary>
    public long TotalBytes { get; }

    /// <summary><see cref="Cache{K,T}.SizeCapacity"/> for the decoded display-tier cache.</summary>
    public long DisplayBytes { get; }

    /// <summary>
    /// <see cref="Cache{K,T}.SizeCapacity"/> for the decoded strip-band cache (webtoon long strips in the continuous modes):
    /// a quarter of the display budget, but never under <see cref="BandFloor"/> (or the whole display budget, if that is smaller).
    /// A band is 4,096 rows, so 13 MiB for an 800 px wide strip and 26 MiB at 1,600 px, and the reader keeps the visible
    /// bands, one margin band each side and one to three prefetched ones. The quarter alone was sized against the old 512 MiB
    /// budget (120 MiB); when the Auto budget was lowered on 2026-10-08 it fell to 56 MiB, too small for even the visible
    /// and margin bands of an ordinary strip, so bands were evicted and decoded again while still on screen.
    /// </summary>
    public long BandBytes { get; }

    /// <summary>The smallest strip-band cache: about what the quarter gave under the original 512 MiB budget.</summary>
    public const long BandFloor = 128 * Mib;

    /// <summary>
    /// Bands the reader keeps for one strip at a time: the most <see cref="StripBandPlan"/> ever keeps (two on screen plus its
    /// keep distance each side). The cache has to hold all of them, or it evicts by size what the plan just asked for.
    /// </summary>
    public const int ResidentBands = StripBandPlan.MaxResidentBands;

    /// <summary>No strip gets a band cache above this, however wide: 512 MiB is ten bands of a 3,200 px wide strip.</summary>
    public const long BandCeiling = 512 * Mib;

    /// <summary>
    /// The band cache a strip of <paramref name="stripWidth"/> pixels needs: room for <see cref="ResidentBands"/> of its own
    /// bands, never under <see cref="BandBytes"/> and never over <see cref="BandCeiling"/>. Webtoon strips are usually 720-800 px
    /// wide (13 MiB a band), but raws and high-quality releases are 1,600 px and more (26 MiB and up), where a fixed size
    /// that suits the narrow ones evicts bands still on screen. The cost follows what is being read: 125 MiB at 800 px,
    /// 250 MiB at 1,600 px.
    /// </summary>
    public long BandBytesFor(int stripWidth, int bandHeight)
    {
        long needed = (long)ResidentBands * Math.Max(1, stripWidth) * Math.Max(1, bandHeight) * 4;
        return Math.Max(BandBytes, Math.Min(BandCeiling, needed));
    }

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
