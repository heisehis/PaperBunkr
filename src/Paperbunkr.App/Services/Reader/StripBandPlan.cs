using System;

namespace Paperbunkr.App.Services.Reader;

/// <summary>Where a strip page is against the viewport.</summary>
internal enum StripReach
{
    /// <summary>Part of it is on screen.</summary>
    Visible,

    /// <summary>Off screen, within one viewport length: it could scroll in on the next gesture.</summary>
    Near,

    /// <summary>Further away than that.</summary>
    Far,
}

/// <summary>
/// Which bands of a long-strip page to decode now, which to prefetch, and which to keep, given the bands on screen
/// (docs/superpowers/specs/2026-09-09-reader-webtoon-strip-band-decode-design.md §4.1). Pulled out of
/// <c>ReaderImagePipeline.SetStripBandWindow</c> on 2026-10-10 so the one rule that matters can be stated and tested:
/// <b>everything asked for is kept.</b>
///
/// The inline version prefetched up to four bands beyond the screen and evicted anything more than two away. Whenever the
/// prefetch reached past two, each call (one per scroll tick) threw out the bands the previous call had just prefetched and
/// queued them again: a decode-evict loop that ran for as long as the strip was on screen. Every evicted band then sat in the
/// pipeline's four-second deferred-dispose queue, so the faster bands decoded, the more of them were alive at once. Measured:
/// 150 band buffers, 1.7 GB, on a 720 px strip.
///
/// Bands are kept one further out than they are prefetched, so a small scroll back and forth across a band edge does not
/// evict and re-request the outermost band either.
/// </summary>
internal readonly record struct StripBandPlan(int DecodeMin, int DecodeMax, int PrefetchMin, int PrefetchMax, int KeepMin, int KeepMax)
{
    /// <summary>Bands either side of the screen decoded at high priority.</summary>
    public const int Margin = 1;

    /// <summary>Most bands prefetched beyond the margin, each side. Was three; one keeps a strip on screen at eight bands.</summary>
    public const int MaxPrefetch = 1;

    /// <summary>Bands kept beyond the furthest prefetch, each side.</summary>
    public const int KeepSlack = 1;

    /// <summary>Bands on screen at once that the cache is sized for (a band is 4,096 source rows; two straddle a band edge).</summary>
    public const int VisibleBands = 2;

    /// <summary>Bands kept for a strip that is just off screen (<see cref="EdgeOnly"/>): its edge band and one of slack.</summary>
    public const int EdgeBands = 2;

    /// <summary>
    /// The most bands that can be wanted at once: the strip on screen, plus a strip just off screen above and one below.
    /// The band cache is sized to this (<see cref="ReaderMemoryBudget.ResidentBands"/>); if it held fewer, it would evict by
    /// size what the plan had just asked for.
    /// </summary>
    public const int MaxResidentBands = VisibleBands + (2 * (Margin + MaxPrefetch + KeepSlack)) + (2 * EdgeBands);

    /// <summary>The plan for a strip just off screen: exactly the band(s) given, no margin, no prefetch, one band of slack kept.</summary>
    public static StripBandPlan EdgeOnly(int minBand, int maxBand) => new(
        DecodeMin: Math.Max(0, minBand),
        DecodeMax: maxBand,
        PrefetchMin: Math.Max(0, minBand),
        PrefetchMax: maxBand,
        KeepMin: minBand - KeepSlack,
        KeepMax: maxBand + KeepSlack);

    /// <summary>
    /// Which bands of a strip are in the viewport, from the strip's placed extent along the scroll axis (display units) and
    /// the viewport's. On screen: the bands the viewport overlaps. Just off screen (within one viewport length): only the band
    /// at the edge it will enter by. Further away: nothing.
    /// </summary>
    public static (StripReach Reach, int MinBand, int MaxBand) BandsInView(
        double stripStart, double displayLength, double nativeLength, double viewportStart, double viewportEnd, int bandHeight)
    {
        if (nativeLength <= 0 || displayLength <= 0 || bandHeight <= 0)
        {
            return (StripReach.Far, 0, 0);
        }

        int bandCount = Math.Max(1, (int)Math.Ceiling(nativeLength / bandHeight));
        double stripEnd = stripStart + displayLength;
        double visibleStart = Math.Max(stripStart, viewportStart) - stripStart;
        double visibleEnd = Math.Min(stripEnd, viewportEnd) - stripStart;
        if (visibleEnd > visibleStart)
        {
            double scale = displayLength / nativeLength;
            int min = Math.Clamp((int)Math.Floor(visibleStart / scale / bandHeight), 0, bandCount - 1);
            int max = Math.Clamp((int)Math.Floor(visibleEnd / scale / bandHeight), 0, bandCount - 1);
            return (StripReach.Visible, min, max);
        }

        double viewportLength = Math.Max(0, viewportEnd - viewportStart);
        if (stripStart >= viewportEnd && stripStart - viewportEnd <= viewportLength)
        {
            return (StripReach.Near, 0, 0); // below (or after): it enters by its first band
        }

        if (stripEnd <= viewportStart && viewportStart - stripEnd <= viewportLength)
        {
            return (StripReach.Near, bandCount - 1, bandCount - 1); // above (or before): it enters by its last band
        }

        return (StripReach.Far, 0, 0);
    }

    /// <param name="fringeAboveMinimum">How far the pipeline's adaptive page fringe is above its minimum (0-4): a fast decoder earns a deeper prefetch.</param>
    public static StripBandPlan For(int minBand, int maxBand, int fringeAboveMinimum)
    {
        int prefetch = 1 + Math.Clamp(fringeAboveMinimum / 2, 0, MaxPrefetch - 1); // 1..MaxPrefetch
        int reach = Margin + prefetch;
        return new StripBandPlan(
            DecodeMin: Math.Max(0, minBand - Margin),
            DecodeMax: maxBand + Margin,
            PrefetchMin: Math.Max(0, minBand - reach),
            PrefetchMax: maxBand + reach,
            KeepMin: minBand - reach - KeepSlack,
            KeepMax: maxBand + reach + KeepSlack);
    }
}
