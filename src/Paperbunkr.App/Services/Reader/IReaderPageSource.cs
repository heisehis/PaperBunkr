using System;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// The single decode/cache/prefetch seam behind <c>PageCanvas.Decoder</c> (docs/superpowers/specs/
/// 2026-09-08-reader-decode-cache-prefetch-pipeline-design.md §3). Supersedes the paged-only
/// <see cref="IPageImageDecoder"/> and the continuous-only <c>PageDecodeService</c> surface, so
/// paged, continuous and PDF all drive one implementation (<see cref="ReaderImagePipeline"/>).
///
/// Names match the members <c>PageCanvas</c> already called on <c>PageDecodeService</c> so the
/// continuous render path changes only its <c>is</c>-check, not its calls.
/// </summary>
public interface IReaderPageSource : IPageImageDecoder
{
    /// <summary>Live decoded display-tier bitmap count - the observable "decoded pages are a hard-bounded resource" signal, asserted in the memory-bound tests.</summary>
    int DecodedPageCount { get; }

    /// <summary>Centre of the last <see cref="SetVirtualizationWindow"/> call - "the page the reader is looking at". -1 before the first call. Used by the detail tier (§6.2) so the renderer needn't carry a page index of its own.</summary>
    int ActivePageIndex { get; }

    /// <summary>Target width future display-tier decodes downsample to. Doesn't rescale already-cached pages.</summary>
    void SetViewportWidth(int width);

    /// <summary>
    /// Declares the pages on/near screen right now. The pipeline decodes this range at high
    /// priority plus an adaptive prefetch fringe at low priority (§8), and evicts decoded bitmaps
    /// for anything well outside it. Cheap to call every frame; idempotent.
    /// </summary>
    void SetVirtualizationWindow(int minIndex, int maxIndex);

    /// <summary>Non-blocking display-tier cache peek - the page if already decoded, else <see langword="null"/> (caller draws a gap and waits for <see cref="BackgroundDecodeCompleted"/>).</summary>
    Bitmap? TryGetCachedPage(int pageIndex);

    /// <summary>Raised (off the UI thread) when a queued page lands in the display cache - subscribers marshal to the UI thread and re-query.</summary>
    event Action<int> BackgroundDecodeCompleted;

    /// <summary>On-demand higher-resolution decode for zoom-past-100% (§6.2). Never cached; caller disposes when zoom settles. Phase 2 wires a real trigger; present now so the seam is stable.</summary>
    Bitmap GetDetailPage(int pageIndex, PixelSize targetSize);

    /// <summary>
    /// Releases the byte-budget reservation the last <see cref="GetDetailPage"/> took against the
    /// display cache (design §15 #3). Call when the detail bitmap is no longer shown (zoom back to
    /// fit, page turn, reader close). Idempotent; a no-op when nothing is reserved. The reservation
    /// also self-releases on the next <see cref="GetDetailPage"/> or a page change.
    /// </summary>
    void ReleaseDetail();

    /// <summary>
    /// Holds the low-priority prefetch-fringe decode for <paramref name="milliseconds"/> — the
    /// visible window still decodes at high priority. Called while a paged page-turn transition is
    /// animating so the fringe's `CreateScaledBitmap` churn on the decode workers doesn't starve
    /// the compositor mid-slide. Re-arming or a shorter value both take effect immediately.
    /// </summary>
    void SuppressFringePrefetch(int milliseconds);

    /// <summary>
    /// Requests a header-only page-size peek (docs/superpowers/specs/2026-09-09-reader-webtoon-
    /// strip-band-decode-design.md §4.3) — fire-and-forget, never blocks the calling thread. A
    /// no-op if the size is already known. <see cref="PageSizeAvailable"/> fires once it lands.
    /// </summary>
    void RequestPageSize(int pageIndex);

    /// <summary>Raised (off the UI thread) when a requested page size lands — subscribers marshal to the UI thread, update their own size cache, and re-layout.</summary>
    event Action<int, PixelSize> PageSizeAvailable;

    /// <summary>
    /// Non-blocking display-tier cache peek for one band of a strip page (docs/superpowers/specs/
    /// 2026-09-09-reader-webtoon-strip-band-decode-design.md §4.1) — the band if already decoded,
    /// else <see langword="null"/> (caller draws a gap and waits for
    /// <see cref="BackgroundDecodeCompleted"/>, same as <see cref="TryGetCachedPage"/>). A page that
    /// isn't actually a strip, or a strip whose format doesn't support band decode (PNG, in
    /// practice — §4.1's finding), never has cached bands; callers should have routed those through
    /// the ordinary whole-page path instead.
    /// </summary>
    Bitmap? TryGetCachedBand(int pageIndex, int band);

    /// <summary>
    /// Declares which bands of one strip page are actually near the viewport right now (design
    /// §4.2) — the sub-page-range analogue of <see cref="SetVirtualizationWindow"/>, needed because
    /// that method only carries whole-page granularity and a single strip page can span far more of
    /// the viewport than that. The pipeline decodes <paramref name="minBand"/>..<paramref name="maxBand"/>
    /// plus a small margin at high priority, prefetches a bit further via the same adaptive fringe
    /// whole-page decode already uses, and evicts bands well outside that range. A no-op for a page
    /// that isn't a strip (or isn't currently in the window <see cref="SetVirtualizationWindow"/>
    /// last declared).
    /// </summary>
    void SetStripBandWindow(int pageIndex, int minBand, int maxBand);

    /// <summary>
    /// Non-blocking: whether <paramref name="pageIndex"/> is currently known to be a strip whose
    /// format actually supports band decode (design §4.1/§4.2) - never peeks or triggers I/O if
    /// unknown, just reports <see langword="false"/> (the caller should then route this page as an
    /// ordinary whole page for this one frame - the same "safe default until classified" shape
    /// <see cref="SetVirtualizationWindow"/> itself already uses internally).
    /// </summary>
    bool IsKnownBandableStrip(int pageIndex);

    /// <summary>The fixed source-pixel band size band decode uses (design §4.1.1) - <see cref="Paperbunkr.App.Views.PageCanvas"/> needs this to convert a strip's visible pixel sub-range into band indices for <see cref="SetStripBandWindow"/>.</summary>
    int BandHeight { get; }
}
