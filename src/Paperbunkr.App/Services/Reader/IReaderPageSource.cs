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
}
