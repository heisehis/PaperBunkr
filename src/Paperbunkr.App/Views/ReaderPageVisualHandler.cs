using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using SkiaSharp;

namespace Paperbunkr.App.Views;

/// <summary>
/// Immutable per-frame draw data for <see cref="ReaderPageVisualHandler"/>, sent from the UI
/// thread via <see cref="CompositionCustomVisual.SendHandlerMessage"/>. Bundling every value the
/// handler needs into one message (rather than mutable fields the handler reads directly) means
/// there's no shared mutable state between the UI thread (<see cref="PageCanvas"/>, which
/// constructs one of these per property change) and the compositor thread (which only ever reads a
/// given instance after receiving it) - same "construct fresh, hand off, never mutate after" shape
/// <see cref="ReaderPageDrawOperation"/> (this handler's paged-mode predecessor,
/// docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §4)
/// already had via its constructor.
/// </summary>
internal sealed record ReaderPageVisualData(
    Rect Bounds,
    Bitmap? Bitmap,
    bool HighQuality,
    double Zoom,
    double PanOffsetX,
    double PanOffsetY,
    ImageFitMode FitMode,
    bool FitOnlyIfOversized,
    int RotationDegrees,
    Bitmap? SecondaryBitmap = null,
    bool IsRightToLeft = false,
    // Item 1 §1.5 of docs/superpowers/specs/2026-09-10-reader-backlog-batch-b-design.md - drawn
    // behind the page/spread rect, texture-background mode only.
    bool ShowShadow = false,
    ReaderPageFrameStyle FrameStyle = ReaderPageFrameStyle.None,
    Color FrameColor = default,
    bool BookLook = false,
    double BookProgress = 0);

/// <summary>
/// One page's already-computed on-screen placement (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §2/§4) - <see cref="Rect"/> comes straight from <see cref="ReaderLayoutModel.ComputeContinuousLayout"/>, already in viewport space, so the handler just scales-and-draws rather than repeating fit/pan math per page.
/// <see cref="Bands"/> (docs/superpowers/specs/2026-09-09-reader-webtoon-strip-band-decode-design.md
/// §4.3), when non-<see langword="null"/>, means this entry is a webtoon strip decoded in bands
/// rather than as one whole bitmap - <see cref="Bitmap"/> is then unused (always
/// <see langword="null"/> for a strip entry) and <see cref="RenderContinuous"/> draws each slot in
/// <see cref="Bands"/> instead, using <see cref="Rect"/> only for the whole-strip visibility
/// quick-reject.
/// </summary>
internal readonly record struct ContinuousPageEntry(Rect Rect, Bitmap? Bitmap, IReadOnlyList<StripBandSlot>? Bands = null);

/// <summary>One band's already-computed on-screen placement within a strip (design §4.1.2) - <see cref="Rect"/> is derived from one continuous coordinate mapping across every band of the strip (not independently rounded per band), specifically to avoid a hairline seam where adjacent bands meet.</summary>
internal readonly record struct StripBandSlot(Rect Rect, Bitmap? Bitmap);

/// <summary>Continuous mode's per-frame draw data - unlike <see cref="ReaderPageVisualData"/>'s single bitmap, a whole page list, each with its own placement. No fit-mode/rotation fields - continuous mode has neither (spec §5/§9's named scope, rotation isn't part of this pass for continuous mode).</summary>
internal sealed record ReaderContinuousVisualData(Size Bounds, IReadOnlyList<ContinuousPageEntry> Pages, bool HighQuality,
    ReaderPageFrameStyle FrameStyle = ReaderPageFrameStyle.None, Color FrameColor = default);

/// <summary>
/// Which spatial edge a paged-mode page-turn animates from/to (docs/superpowers/specs/
/// 2026-08-13-reader-page-transition-animations-design.md §3.1/§3.2) - keyed off <see cref="PageCanvas"/>'s
/// existing spatial Left/Right naming (docs/superpowers/specs/2026-08-07-reader-rtl-navigation-
/// design.md §3), not reading-direction semantics. Internal, not persisted - distinct from the one
/// persisted setting, <see cref="Data.Entities.PageTransitionStyle"/>.
/// </summary>
internal enum PageTransitionDirection
{
    Left,
    Right,
    Up,
    Down
}

/// <summary>
/// An in-flight paged-mode page-turn animation (docs/superpowers/specs/2026-08-13-reader-page-
/// transition-animations-design.md §3.2), sent by <see cref="PageCanvas"/> instead of
/// <see cref="ReaderPageVisualData"/> when an adjacent turn should animate. Both bitmaps render
/// against the current (already-updated) zoom/pan/fit/rotation state, independently placed by their
/// own pixel size - not an attempt to preserve either page's historical transform.
/// <see cref="OldIsRightToLeft"/>/<see cref="IsRightToLeft"/> are usually equal (reading direction
/// doesn't change mid-turn) - split into two fields specifically so an RTL toggle while a spread is
/// showing (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md §6) can animate the
/// same two bitmaps mirroring from their old left/right placement to their new one, not just fade in
/// place (which - same bitmaps, same position - would be visually a no-op).
/// </summary>
internal sealed record ReaderPageTransitionData(
    Rect Bounds,
    Bitmap? OldBitmap,
    Bitmap? NewBitmap,
    bool HighQuality,
    double Zoom,
    double PanOffsetX,
    double PanOffsetY,
    ImageFitMode FitMode,
    bool FitOnlyIfOversized,
    int RotationDegrees,
    Data.Entities.PageTransitionStyle Style,
    TimeSpan Duration,
    PageTransitionDirection Direction,
    Bitmap? OldSecondaryBitmap = null,
    Bitmap? NewSecondaryBitmap = null,
    bool IsRightToLeft = false,
    bool OldIsRightToLeft = false,
    bool BookLook = false,
    double BookProgress = 0);

/// <summary>
/// Live brightness/contrast/saturation/gamma (docs/superpowers/specs/2026-08-10-reader-polish-
/// continuous-scroll-chrome-overlays-design.md §9) - a separate, independently-updated message from
/// <see cref="ReaderPageVisualData"/>/<see cref="ReaderContinuousVisualData"/> (orthogonal to
/// paged-vs-continuous mode, applies identically to either), so <see cref="PageCanvas"/> can push a
/// new value here without needing to also resend whatever page data was last pushed. Values are
/// each -100..100 (the raw Preferences/toolbar slider range) - see
/// <see cref="ImageAdjustmentMath.CreateColorMatrix"/>'s own doc comment for where that gets
/// normalized. Record equality (all-default fields) backs
/// <see cref="ReaderPageVisualHandler"/>'s color-filter cache, so dragging a slider that ends up
/// back where it started doesn't rebuild a filter unnecessarily.
/// </summary>
internal sealed record AdjustmentVisualData(double Brightness, double Contrast, double Saturation, double Gamma, double Warmth = 0, int Sharpen = 0)
{
    public static readonly AdjustmentVisualData None = new(0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Draws a single decoded page, letterboxed (uniform-fit, centered) within the given bounds -
/// paged mode's renderer, now on the composition-visual pipeline shared with continuous mode
/// (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md
/// §1/§4 - the deliberate deviation from onboarding.md §8's original two-mechanism split, made at
/// the user's explicit direction). Runs on the compositor/render thread, not the UI thread -
/// <see cref="OnMessage"/> receives a fresh <see cref="ReaderPageVisualData"/> each time
/// <see cref="PageCanvas"/> pushes a change, <see cref="OnRender"/> draws whatever the latest one
/// was. Doesn't own <paramref name="Bitmap"/>'s lifetime - <see cref="Services.PageImageDecoder"/>/
/// <see cref="Services.PageDecodeService"/> does, same as the paged-mode predecessor this replaces.
/// </summary>
public sealed class ReaderPageVisualHandler : CompositionCustomVisualHandler
{
    private ReaderPageVisualData? _pagedData;
    private ReaderContinuousVisualData? _continuousData;
    private AdjustmentVisualData _adjustmentData = AdjustmentVisualData.None;

    /// <summary>An in-flight page-turn animation, if any (spec §3.2) - mutually exclusive with <see cref="_pagedData"/>/<see cref="_continuousData"/> while active, collapsed back into <see cref="_pagedData"/> once <see cref="OnAnimationFrameUpdate"/> sees it complete.</summary>
    private ReaderPageTransitionData? _transitionData;

    private TimeSpan? _transitionStart;

    /// <summary>
    /// Real bug, found via manual testing: a naive first version called
    /// <see cref="SkiaBitmapConverter.ToSkImage"/> fresh inside <see cref="DrawTransitionBitmap"/> on
    /// every single <see cref="OnRender"/> call - <see cref="SkiaBitmapConverter.ToSkImage"/> is not a
    /// cheap wrap, it's a full-resolution pixel copy (a <see cref="WriteableBitmap"/> lock +
    /// <c>Marshal.Copy</c>, then <c>SKData.CreateCopy</c>'s own second copy) - for a Crossfade turn
    /// (the only style that needs it, per <see cref="DrawBitmap"/>'s own lease-path condition) that
    /// meant two full-page pixel copies *per frame*, sustained for the whole animation, visibly choppy
    /// in practice. Fixed by converting each bitmap exactly once when the transition starts (here, in
    /// <see cref="OnMessage"/>) and reusing the same <see cref="SKImage"/> across every frame of that
    /// transition - disposed whenever the transition ends or a new one/plain page replaces it.
    /// </summary>
    private SKImage? _transitionOldImage;
    private SKImage? _transitionNewImage;

    /// <summary>Spread-side counterparts to <see cref="_transitionOldImage"/>/<see cref="_transitionNewImage"/> (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md §5) - null whenever that side of the turn wasn't a paired spread.</summary>
    private SKImage? _transitionOldSecondaryImage;
    private SKImage? _transitionNewSecondaryImage;

    private void DisposeTransitionImages()
    {
        _transitionOldImage?.Dispose();
        _transitionNewImage?.Dispose();
        _transitionOldSecondaryImage?.Dispose();
        _transitionNewSecondaryImage?.Dispose();
        _transitionOldImage = null;
        _transitionNewImage = null;
        _transitionOldSecondaryImage = null;
        _transitionNewSecondaryImage = null;
    }

    /// <summary>
    /// Rebuilt only when <see cref="_adjustmentData"/> actually changes (record equality), not every
    /// compose pass - <see cref="SKColorFilter"/> construction is cheap but not free, and this runs
    /// on every render while a live-adjustment panel is open. Left for the finalizer/GC to reclaim
    /// rather than explicitly disposed on replacement - SkiaSharp's native ref-counting handles a
    /// dropped reference safely, and this is rebuilt rarely enough (debounced by nothing on this
    /// side, but a real user only drags a slider so many times) that explicit disposal isn't worth
    /// the risk of a double-free against whatever <see cref="SKColorFilter.CreateCompose"/> does
    /// internally with its two input filters.
    /// </summary>
    private SKColorFilter? _cachedColorFilter;
    private AdjustmentVisualData? _cachedFor;

    /// <summary>
    /// Sharpening (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md #2): CE's 3x3 cross kernel as a paint-level Skia matrix convolution, applied after the colour filter to the page as drawn
    /// (so it sharpens what is on screen, at any zoom). Null while the level is 0; built once per level and replaced only when the level changes.
    /// </summary>
    private SKImageFilter? _sharpenFilter;
    private int _sharpenFilterLevel;

    private void RefreshSharpenFilter()
    {
        int level = Math.Clamp(_adjustmentData.Sharpen, 0, ImageAdjustmentMath.MaxSharpen);
        if (level == _sharpenFilterLevel)
        {
            return;
        }

        _sharpenFilterLevel = level;
        _sharpenFilter = level == 0
            ? null
            : SKImageFilter.CreateMatrixConvolution(new SKSizeI(3, 3), ImageAdjustmentMath.CreateSharpenKernel(level), 1f, 0f, new SKPointI(1, 1), SKShaderTileMode.Clamp, convolveAlpha: false);
    }

    /// <summary>Whether draws must go through the leased Skia canvas: a colour filter or sharpening needs a paint the plain Avalonia draw call cannot carry.</summary>
    private bool NeedsPaint(SKColorFilter? colorFilter) => colorFilter is not null || _sharpenFilter is not null;

    /// <summary>
    /// How a page bitmap is sampled when the leased canvas draws it (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md #25). A bare <c>SKPaint</c> samples with nearest-neighbour, which
    /// aliases print screentone into moire (measured: about twice the shimmer of a mipmapped draw when the page is drawn smaller than its bitmap) and blocks the page when it is enlarged. High quality
    /// downscales through mipmaps (the same anti-aliased result <c>CreateScaledBitmap(HighQuality)</c> gives the unfiltered path) and enlarges with a cubic; low quality is plain bilinear.
    /// </summary>
    internal static SKSamplingOptions SamplingFor(bool highQuality, double drawWidth, double sourceWidth)
    {
        if (!highQuality)
        {
            return new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        }

        return drawWidth < sourceWidth
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);
    }

    /// <summary>
    /// Real perf bug, found while chasing continuous-scroll tearing: <see cref="RenderContinuous"/>'s
    /// original body called <see cref="Bitmap.CreateScaledBitmap"/> for every visible page on every
    /// single <see cref="OnRender"/> pass - a full-resolution Skia resample plus a fresh bitmap
    /// allocation per page per compose frame, sustained for the whole duration of a scroll. On a
    /// high-refresh display that blew the render-thread frame budget and showed up as tearing/judder.
    /// A scroll changes each page's on-screen <see cref="ContinuousPageEntry.Rect"/> *position* every
    /// frame but not its *size* (that only moves on zoom / viewport resize), so caching the
    /// downscaled bitmap keyed by the source <see cref="Bitmap"/> means a pure scroll re-resamples
    /// nothing - every still-visible page is a cache hit. Keyed by source-bitmap reference rather
    /// than page index so it needs nothing added to <see cref="ContinuousPageEntry"/>; entries whose
    /// source is no longer in the visible set are disposed at the end of each render pass. Only
    /// populated for a genuine downscale - an at-or-above-native draw (the default fit-width zoom)
    /// skips the intermediate bitmap entirely and lets the compositor scale on the GPU.
    /// </summary>
    private readonly Dictionary<Bitmap, (PixelSize Size, Bitmap Scaled)> _continuousScaledCache = new();

    /// <summary>
    /// Phase 4 (docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md
    /// §10): when a live image adjustment is on, both <see cref="RenderContinuous"/> and
    /// <see cref="DrawBitmap"/>'s lease path used to call <see cref="SkiaBitmapConverter.ToSkImage"/>
    /// - two full-resolution pixel copies - for every drawn page on <b>every</b> compose frame,
    /// sustained for the whole time the adjustment panel is open. Keyed by source
    /// <see cref="Bitmap"/> reference and evicted alongside <see cref="_continuousScaledCache"/>, so
    /// a pure scroll / a pan with adjustment on re-copies nothing.
    /// </summary>
    private readonly Dictionary<Bitmap, SKImage> _skImageCache = new();

    private SKImage GetOrCreateSkImage(Bitmap source)
    {
        if (_skImageCache.TryGetValue(source, out var cached))
        {
            return cached;
        }

        // Shares the page's own pixels (2026-10-08): the copy this used to make kept a second full page per visible page.
        var image = SkiaBitmapConverter.ShareSkImage(source);
        _skImageCache[source] = image;
        return image;
    }

    /// <summary>
    /// <see cref="SkiaBitmapConverter.ToSkImage"/> guarded against a bitmap that's been disposed
    /// between the pipeline handing it out and this message being processed - returns
    /// <see langword="null"/> (the page just doesn't draw this frame) rather than propagating a
    /// managed <see cref="ObjectDisposedException"/> onto the compositor thread.
    /// </summary>
    private static SKImage? SafeToSkImage(Bitmap bitmap)
    {
        try
        {
            return SkiaBitmapConverter.ShareSkImage(bitmap);
        }
        catch
        {
            return null;
        }
    }

    /// <param name="keepFor">When non-null, SKImage cache entries whose source bitmap is still one of this data's bitmaps are kept (a pan/zoom that didn't change the page - §10); everything else is disposed.</param>
    private void DisposeContinuousScaledCache(ReaderPageVisualData? keepFor = null)
    {
        foreach (var cached in _continuousScaledCache.Values)
        {
            cached.Scaled.Dispose();
        }

        _continuousScaledCache.Clear();

        foreach (var (bmp, image) in _skImageCache)
        {
            if (keepFor is { } k && (ReferenceEquals(bmp, k.Bitmap) || ReferenceEquals(bmp, k.SecondaryBitmap)))
            {
                continue;
            }
            image.Dispose();
            _skImageCacheEvicted.Add(bmp);
        }

        foreach (var bmp in _skImageCacheEvicted)
        {
            _skImageCache.Remove(bmp);
        }
        _skImageCacheEvicted.Clear();
    }

    private readonly List<Bitmap> _skImageCacheEvicted = new();

    /// <summary>
    /// Runs on the compositor thread (confirmed via reflection against the actual Avalonia 12.1.1
    /// assembly this project targets, not assumed - <see cref="CompositionCustomVisual.SendHandlerMessage"/>
    /// marshals across the UI-thread/compositor-thread boundary, landing here). Stores whichever
    /// message type arrived (mutually exclusive - <see cref="PageCanvas"/> only ever sends one kind
    /// per its current <see cref="Data.Entities.ReadingMode"/>) and calls the base class's
    /// <see cref="Invalidate()"/> to request a redraw on the next compose pass - the composition-
    /// visual equivalent of what <c>AffectsRender&lt;PageCanvas&gt;</c> did for the old
    /// <c>DrawingContext</c>-based renderer this replaces.
    /// </summary>
    public override void OnMessage(object message)
    {
        switch (message)
        {
            case ReaderPageVisualData paged:
            {
                // Keep the SKImage cache across a pan/zoom that doesn't change the bitmap(s) - the
                // whole point of §10's per-frame-copy fix for paged mode with adjustment on.
                bool sameBitmaps = _pagedData is { } prev
                    && ReferenceEquals(prev.Bitmap, paged.Bitmap)
                    && ReferenceEquals(prev.SecondaryBitmap, paged.SecondaryBitmap);
                _pagedData = paged;
                _continuousData = null;
                _transitionData = null;
                _transitionStart = null;
                DisposeTransitionImages();
                DisposeContinuousScaledCache(sameBitmaps ? paged : null);
                Invalidate();
                break;
            }
            case ReaderContinuousVisualData continuous:
                _continuousData = continuous;
                _pagedData = null;
                _transitionData = null;
                _transitionStart = null;
                DisposeTransitionImages();
                Invalidate();
                break;
            case ReaderPageTransitionData transition:
                DisposeTransitionImages();
                DisposeContinuousScaledCache();
                _transitionData = transition;
                _continuousData = null;
                // CompositionNow, not a UI-thread clock - this handler and OnAnimationFrameUpdate
                // both run on the compositor thread (spec §3.2), so progress is measured against the
                // same clock throughout the animation.
                _transitionStart = CompositionNow;

                // Convert BOTH page bitmaps to SKImage copies right now, while the message just
                // arrived and the bitmaps are guaranteed alive - RenderTransition then never reads
                // a raw Avalonia Bitmap per-frame. Previously only Crossfade did this; Slide with a
                // colour filter active hit DrawBitmap's lease path and re-converted the raw bitmap
                // every frame, which AccessViolation'd when the reader pipeline freed one mid-turn.
                _transitionOldImage = transition.OldBitmap is { } oldBitmap ? SafeToSkImage(oldBitmap) : null;
                _transitionNewImage = transition.NewBitmap is { } newBitmap ? SafeToSkImage(newBitmap) : null;
                _transitionOldSecondaryImage = transition.OldSecondaryBitmap is { } oldSecondary ? SafeToSkImage(oldSecondary) : null;
                _transitionNewSecondaryImage = transition.NewSecondaryBitmap is { } newSecondary ? SafeToSkImage(newSecondary) : null;

                RegisterForNextAnimationFrameUpdate();
                Invalidate();
                break;
            case AdjustmentVisualData adjustment:
                _adjustmentData = adjustment;
                Invalidate();
                break;
        }
    }

    /// <summary>
    /// Drives the page-turn animation forward (spec §3.2) - re-registers itself every frame while
    /// still in progress; once <see cref="ComputeTransitionProgress"/> reaches 1, collapses the
    /// transition into an equivalent steady-state <see cref="ReaderPageVisualData"/> (the new bitmap,
    /// same zoom/pan/fit/rotation) so a later resize/zoom change re-renders through the ordinary
    /// non-animated path, with no lingering transition state.
    /// </summary>
    public override void OnAnimationFrameUpdate()
    {
        if (_transitionData is null && _continuousReveals.Count > 0)
        {
            Invalidate();
            return;
        }

        if (_transitionData is not { } data || _transitionStart is null)
        {
            return;
        }

        if (ComputeTransitionProgress() < 1)
        {
            Invalidate();
            RegisterForNextAnimationFrameUpdate();
            return;
        }

        _pagedData = new ReaderPageVisualData(data.Bounds, data.NewBitmap, data.HighQuality, data.Zoom,
            data.PanOffsetX, data.PanOffsetY, data.FitMode, data.FitOnlyIfOversized, data.RotationDegrees,
            data.NewSecondaryBitmap, data.IsRightToLeft);
        _transitionData = null;
        _transitionStart = null;
        DisposeTransitionImages();
        Invalidate();
    }

    private double ComputeTransitionProgress()
    {
        if (_transitionData is not { } data || _transitionStart is null || data.Duration <= TimeSpan.Zero)
        {
            return 1;
        }

        double elapsedMs = (CompositionNow - _transitionStart.Value).TotalMilliseconds;
        return Math.Clamp(elapsedMs / data.Duration.TotalMilliseconds, 0, 1);
    }

    /// <summary>Falls back to the visual's own <see cref="CompositionCustomVisualHandler.EffectiveSize"/> before the first message arrives, so the compositor has a sane bounds to reason about even on the very first compose pass.</summary>
    public override Rect GetRenderBounds() => _transitionData?.Bounds ?? _pagedData?.Bounds ?? (_continuousData is { } c ? new Rect(c.Bounds) : new Rect(0, 0, EffectiveSize.X, EffectiveSize.Y));

    /// <summary>
    /// Live image adjustment (spec §9) - a paint-level <see cref="SKColorFilter"/>, not a pixel-
    /// buffer mutation, so it updates every compose pass with no redecode of the underlying page.
    /// Real bug, found via manual testing: the first version of this wrapped the existing Avalonia-
    /// level <c>context.DrawBitmap</c> calls in an <c>SKCanvas.SaveLayer</c> pushed on the leased
    /// canvas (<see cref="ISkiaSharpApiLeaseFeature"/>) - moving any slider off zero produced a
    /// totally blank frame. Avalonia's own custom-Skia-rendering samples treat a leased
    /// <see cref="SKCanvas"/> and Avalonia's own <c>DrawingContext</c>-level draw calls as two
    /// separate rendering strategies, not something to interleave in the same pass - confirmed via
    /// Avalonia's own <c>RenderDemo</c> sample, which never mixes them. Fixed by drawing entirely
    /// through the leased canvas instead, once a filter is active: <see cref="SkiaBitmapConverter"/>
    /// turns the current page's already-decoded <see cref="Bitmap"/> into a raw <see cref="SKImage"/>
    /// (Avalonia's own Skia bitmap wrapper is internal, so there's no direct handle to reach), then
    /// <see cref="RenderPaged"/>/<see cref="RenderContinuous"/> draw that via
    /// <c>SKCanvas.DrawImage(..., SKPaint)</c> with the filter set on the paint - no
    /// <c>context.DrawBitmap</c> call happens at all while a lease is held. Falls back to the
    /// original <c>context.DrawBitmap</c> path (completely untouched) whenever the adjustment is
    /// identity, the common case for most reading sessions.
    /// </summary>
    public override void OnRender(ImmediateDrawingContext context)
    {
        var frameSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            RenderCore(context);
        }
        finally
        {
            frameSw.Stop();
            Services.Reader.ReaderPerfStats.Current.RecordComposeFrameMs(frameSw.Elapsed.TotalMilliseconds);
        }
    }

    private void RenderCore(ImmediateDrawingContext context)
    {
        var colorFilter = GetColorFilter();
        RefreshSharpenFilter();

        // Real bug, found 2026-09-12 via manual testing: the page-shadow (Item 1 §1.5) drew via
        // plain context.DrawRectangle from inside RenderPaged/RenderSpread, *after* the lease
        // below was already checked out whenever a colour filter was active (which is most of the
        // time - Preferences' own default gamma is non-zero, so the identity short-circuit rarely
        // applies). Avalonia's DrawingContextImpl.CheckLease() throws InvalidOperationException
        // ("The underlying graphics API is currently leased") the instant an ordinary draw call
        // runs while an ISkiaSharpApiLease is held - so every paged/spread frame with texture-mode
        // shadow *and* a live adjustment threw, and OnRender's uncaught exception left that frame
        // (and every one after it, since Invalidate() never got a chance to re-request one) simply
        // undrawn: a blank page, not a crash. Continuous mode never hit this (RenderContinuous has
        // no shadow), which is exactly the split the user reported: paged blank, continuous fine.
        // Fixed by drawing the shadow here, strictly before the lease exists - DrawRectangle's
        // BoxShadows overload genuinely needs no lease (confirmed by reading Avalonia's own
        // DrawingContextImpl.DrawRectangle source), it just can't run *while one from this same
        // context is already checked out*.
        if (_pagedData is { ShowShadow: true } shadowSource)
        {
            DrawPagedShadowsOnly(context, shadowSource);
        }
        if (_pagedData is { FrameStyle: ReaderPageFrameStyle.PaperEdge } framedPage)
        {
            DrawPagedFramesOnly(context, framedPage);
        }
        if (_continuousData is { FrameStyle: ReaderPageFrameStyle.PaperEdge } framedContinuous)
        {
            DrawContinuousFramesOnly(context, framedContinuous);
        }
        if (_continuousData is { FrameStyle: ReaderPageFrameStyle.SoftShadow } shadowedContinuous)
        {
            foreach (var page in shadowedContinuous.Pages) DrawPageShadow(context, page.Rect);
        }

        // A crossfade needs the leased-canvas/SKPaint path regardless of whether a brightness/
        // contrast colorFilter is active - plain ImmediateDrawingContext.DrawBitmap has no opacity
        // overload (confirmed via reflection against the actual Avalonia 12.1.1 assembly), so alpha
        // blending between the outgoing/incoming bitmap is only possible through SKPaint.Color's
        // alpha channel.
        // Any transition takes the leased path now, not just Crossfade: it draws the pre-converted
        // SKImages (OnMessage) rather than the raw page bitmaps, so an in-flight animation survives
        // the reader pipeline freeing a bitmap.
        bool needsLease = NeedsPaint(colorFilter) || _transitionData is not null || _continuousGaps.Count > 0 || _continuousReveals.Count > 0
            || _pagedData is { BookLook: true, SecondaryBitmap: not null };
        using ISkiaSharpApiLease? lease = needsLease && context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is { } leaseFeature
            ? leaseFeature.Lease()
            : null;

        if (_transitionData is { } transition)
        {
            RenderTransition(context, transition, ComputeTransitionProgress(), _transitionOldImage, _transitionNewImage,
                _transitionOldSecondaryImage, _transitionNewSecondaryImage, lease, colorFilter);
        }
        else if (_continuousData is { } continuous)
        {
            RenderContinuous(context, continuous, lease, colorFilter);
        }
        else if (_pagedData is { } paged)
        {
            RenderPaged(context, paged, lease, colorFilter);
        }
    }

    /// <summary>Cached by <see cref="AdjustmentVisualData"/> record equality against <see cref="_cachedFor"/> - only rebuilds the <see cref="SKColorFilter"/> when the actual values changed, not every compose pass.</summary>
    private SKColorFilter? GetColorFilter()
    {
        if (ImageAdjustmentMath.IsIdentity(_adjustmentData.Brightness, _adjustmentData.Contrast, _adjustmentData.Saturation, _adjustmentData.Gamma, _adjustmentData.Warmth))
        {
            _cachedColorFilter = null;
            _cachedFor = _adjustmentData;
            return null;
        }

        if (_cachedColorFilter is not null && _cachedFor == _adjustmentData)
        {
            return _cachedColorFilter;
        }

        var matrix = ImageAdjustmentMath.CreateColorMatrix(_adjustmentData.Brightness, _adjustmentData.Contrast, _adjustmentData.Saturation, _adjustmentData.Warmth);
        SKColorFilter colorFilter = SKColorFilter.CreateColorMatrix(matrix);

        // Gamma is a second pass, composed on top (CE: ApplyAdjustment calls ApplyColorMatrix, then
        // ChangeGamma, sequentially - a LUT remap isn't expressible as part of the linear color
        // matrix itself). Skipped entirely when zero, matching every other identity short-circuit
        // here.
        if (_adjustmentData.Gamma != 0)
        {
            var gammaTable = ImageAdjustmentMath.CreateGammaTable(_adjustmentData.Gamma);
            var identityTable = ImageAdjustmentMath.CreateIdentityTable();
            SKColorFilter gammaFilter = SKColorFilter.CreateTable(identityTable, gammaTable, gammaTable, gammaTable);
            colorFilter = SKColorFilter.CreateCompose(gammaFilter, colorFilter);
        }

        _cachedColorFilter = colorFilter;
        _cachedFor = _adjustmentData;
        return colorFilter;
    }

    /// <summary>
    /// A single bitmap's computed on-screen placement - <see cref="DestRect"/> unrotated/unshifted,
    /// <see cref="CenterX"/>/<see cref="CenterY"/> the point rotation pivots around. Split out of
    /// what used to be <c>RenderPaged</c>'s inline math (docs/superpowers/specs/2026-08-13-reader-
    /// page-transition-animations-design.md §3.2) so <see cref="RenderTransition"/> can reuse it for
    /// two independently-placed bitmaps instead of duplicating the fit/pan/rotation formula.
    /// </summary>
    private readonly record struct PageDrawPlan(Rect DestRect, double CenterX, double CenterY);

    /// <summary>Byte-for-byte the same fit/pan/rotation math this file has always used (originally <see cref="ReaderPageDrawOperation"/>'s predecessor <c>Render</c> method) - just returning the plan instead of drawing directly.</summary>
    private static PageDrawPlan ComputeDrawPlan(Rect bounds, PixelSize pixelSize, double zoom, double panOffsetX, double panOffsetY, ImageFitMode fitMode, bool fitOnlyIfOversized, int rotationDegrees)
    {
        bool swapped = rotationDegrees is 90 or 270;
        var effectivePixelSize = swapped ? new PixelSize(pixelSize.Height, pixelSize.Width) : pixelSize;

        double scale = ZoomPanMath.ComputeBaseScale(bounds.Size, effectivePixelSize, fitMode, fitOnlyIfOversized) * zoom;
        var (panX, panY) = ZoomPanMath.ClampPan(bounds.Size, effectivePixelSize, zoom, panOffsetX, panOffsetY, fitMode, fitOnlyIfOversized);

        double centerX = bounds.X + (bounds.Width / 2) + panX;
        double centerY = bounds.Y + (bounds.Height / 2) + panY;

        double nativeWidth = pixelSize.Width * scale;
        double nativeHeight = pixelSize.Height * scale;
        var destRect = new Rect(centerX - (nativeWidth / 2), centerY - (nativeHeight / 2), nativeWidth, nativeHeight);

        return new PageDrawPlan(destRect, centerX, centerY);
    }

    /// <summary>
    /// Draws one bitmap per <paramref name="plan"/> - only the draw call at the end branches on
    /// whether a <paramref name="lease"/> is active (see <see cref="OnRender"/>'s own doc comment for
    /// why that can't just wrap the existing <c>context.DrawBitmap</c> path). <paramref name="alpha"/>
    /// only has an effect via the leased path - the non-lease fallback (no
    /// <see cref="ISkiaSharpApiLeaseFeature"/> available at all, not expected in practice for this
    /// Skia-backed app) draws fully opaque, same as this file's pre-existing behavior whenever no
    /// lease was available. <paramref name="cachedImage"/>, when supplied (the transition path's
    /// per-transition <see cref="_transitionOldImage"/>/<see cref="_transitionNewImage"/>), is used
    /// and left undisposed instead of converting <paramref name="bitmap"/> fresh - see
    /// <see cref="_transitionOldImage"/>'s own doc comment for why that per-frame conversion was a
    /// real performance bug.
    /// </summary>
    private void DrawBitmap(ImmediateDrawingContext context, Bitmap bitmap, PixelSize pixelSize, PageDrawPlan plan, int rotationDegrees, bool highQuality, ISkiaSharpApiLease? lease, SKColorFilter? colorFilter, double alpha, SKImage? cachedImage = null, Func<Bitmap, SKImage>? skImageResolver = null)
    {
        // Take the leased SKImage path whenever a colour filter or partial alpha needs it, OR a
        // caller handed us a pre-converted SKImage - the latter means "don't touch the raw Bitmap",
        // which matters when the reader pipeline may free it out from under an in-flight animation.
        if (lease is not null && (NeedsPaint(colorFilter) || alpha < 1.0 || cachedImage is not null || skImageResolver is not null))
        {
            bool ownsImage = cachedImage is null && skImageResolver is null;
            SKImage skImage = cachedImage ?? skImageResolver?.Invoke(bitmap) ?? SkiaBitmapConverter.ShareSkImage(bitmap);
            try
            {
                using var paint = new SKPaint { ColorFilter = colorFilter, ImageFilter = _sharpenFilter, IsAntialias = true, Color = new SKColor(255, 255, 255, (byte)(Math.Clamp(alpha, 0, 1) * 255)) };
                var sourceRect = new SKRect(0, 0, pixelSize.Width, pixelSize.Height);
                var destRectSk = new SKRect((float)plan.DestRect.X, (float)plan.DestRect.Y, (float)(plan.DestRect.X + plan.DestRect.Width), (float)(plan.DestRect.Y + plan.DestRect.Height));
                var sampling = SamplingFor(highQuality, plan.DestRect.Width, pixelSize.Width);

                if (rotationDegrees == 0)
                {
                    lease.SkCanvas.DrawImage(skImage, sourceRect, destRectSk, sampling, paint);
                    return;
                }

                int saveCount = lease.SkCanvas.Save();
                lease.SkCanvas.RotateDegrees(rotationDegrees, (float)plan.CenterX, (float)plan.CenterY);
                lease.SkCanvas.DrawImage(skImage, sourceRect, destRectSk, sampling, paint);
                lease.SkCanvas.RestoreToCount(saveCount);
                return;
            }
            finally
            {
                if (ownsImage)
                {
                    skImage.Dispose();
                }
            }
        }

        var targetSize = new PixelSize(Math.Max(1, (int)Math.Round(plan.DestRect.Width)), Math.Max(1, (int)Math.Round(plan.DestRect.Height)));
        var mode = highQuality ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.LowQuality;
        using var scaled = bitmap.CreateScaledBitmap(targetSize, mode);

        if (rotationDegrees == 0)
        {
            context.DrawBitmap(scaled, new Rect(0, 0, targetSize.Width, targetSize.Height), plan.DestRect);
            return;
        }

        double radians = rotationDegrees * Math.PI / 180;
        var rotateAroundCenter = Matrix.CreateTranslation(-plan.CenterX, -plan.CenterY) * Matrix.CreateRotation(radians) * Matrix.CreateTranslation(plan.CenterX, plan.CenterY);
        using (context.PushPostTransform(rotateAroundCenter))
        {
            context.DrawBitmap(scaled, new Rect(0, 0, targetSize.Width, targetSize.Height), plan.DestRect);
        }
    }

    private void RenderPaged(ImmediateDrawingContext context, ReaderPageVisualData data, ISkiaSharpApiLease? lease, SKColorFilter? colorFilter)
    {
        if (data.Bitmap is null || data.Bounds.Width <= 0 || data.Bounds.Height <= 0)
        {
            return;
        }

        if (data.SecondaryBitmap is null)
        {
            var pixelSize = data.Bitmap.PixelSize;
            var plan = ComputeDrawPlan(data.Bounds, pixelSize, data.Zoom, data.PanOffsetX, data.PanOffsetY, data.FitMode, data.FitOnlyIfOversized, data.RotationDegrees);

            // §10: with a colour filter active, the leased path converts to SKImage - cache that
            // across frames (a pan/zoom with adjustment on re-copies nothing) rather than the
            // former per-frame double pixel copy. EvictStaleContinuousScales keys off the visible
            // continuous set; a lone paged bitmap that changes triggers OnMessage's cache dispose.
            DrawBitmap(context, data.Bitmap, pixelSize, plan, data.RotationDegrees, data.HighQuality, lease, colorFilter, alpha: 1.0,
                skImageResolver: NeedsPaint(colorFilter) ? GetOrCreateSkImage : null);
            return;
        }

        RenderSpread(context, data.Bounds, data.Bitmap, data.SecondaryBitmap, data.Zoom, data.PanOffsetX, data.PanOffsetY,
            data.FitMode, data.FitOnlyIfOversized, data.HighQuality, data.IsRightToLeft, lease, colorFilter, offset: default, alpha: 1.0,
            skImageResolver: NeedsPaint(colorFilter) ? GetOrCreateSkImage : null, bookLook: data.BookLook, bookProgress: data.BookProgress);
    }

    /// <summary>
    /// Item 1 §1.5 of docs/superpowers/specs/2026-09-10-reader-backlog-batch-b-design.md - draws
    /// this frame's page-shadow(s), strictly before <see cref="RenderCore"/> takes any Skia lease
    /// (see that method's own doc comment for why the two can't interleave). Recomputes the same
    /// plan <see cref="RenderPaged"/>/<see cref="RenderSpread"/> compute for the actual bitmap
    /// draw - duplicate but cheap (plain arithmetic, no allocation of consequence beyond a record
    /// struct), and keeps this pre-lease pass entirely independent of whatever runs after it.
    /// Deliberately never called for a transition (<see cref="RenderTransition"/>) - a page-turn
    /// slide is brief, and threading the flag through that separate, more complex path wasn't
    /// worth it for this pass.
    /// </summary>
    private static void DrawPagedShadowsOnly(ImmediateDrawingContext context, ReaderPageVisualData data)
    {
        if (data.Bitmap is null || data.Bounds.Width <= 0 || data.Bounds.Height <= 0)
        {
            return;
        }

        if (data.SecondaryBitmap is null)
        {
            var pixelSize = data.Bitmap.PixelSize;
            var plan = ComputeDrawPlan(data.Bounds, pixelSize, data.Zoom, data.PanOffsetX, data.PanOffsetY, data.FitMode, data.FitOnlyIfOversized, data.RotationDegrees);
            DrawPageShadow(context, plan.DestRect);
            return;
        }

        var primaryPixelSize = data.Bitmap.PixelSize;
        var secondaryPixelSize = data.SecondaryBitmap.PixelSize;
        var spreadSize = SpreadLayoutMath.ComputeCombinedSize(primaryPixelSize, secondaryPixelSize);
        var combinedPlan = ComputeDrawPlan(data.Bounds, spreadSize.Combined, data.Zoom, data.PanOffsetX, data.PanOffsetY, data.FitMode, data.FitOnlyIfOversized, rotationDegrees: 0);
        double primaryLeftFraction = data.IsRightToLeft ? 1 - spreadSize.LeftWidthFraction : spreadSize.LeftWidthFraction;
        var (left, right) = SpreadLayoutMath.SplitSpread(combinedPlan.DestRect, primaryLeftFraction);
        DrawPageShadow(context, left);
        DrawPageShadow(context, right);
    }

    private static void DrawPageFrame(ImmediateDrawingContext context, Rect rect, Color color)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(color), 1.25), rect);
    }

    private static void DrawPagedFramesOnly(ImmediateDrawingContext context, ReaderPageVisualData data)
    {
        if (data.Bitmap is null || data.Bounds.Width <= 0 || data.Bounds.Height <= 0) return;
        if (data.SecondaryBitmap is null)
        {
            var plan = ComputeDrawPlan(data.Bounds, data.Bitmap.PixelSize, data.Zoom, data.PanOffsetX, data.PanOffsetY,
                data.FitMode, data.FitOnlyIfOversized, data.RotationDegrees);
            DrawPageFrame(context, plan.DestRect, data.FrameColor);
            return;
        }

        var spread = SpreadLayoutMath.ComputeCombinedSize(data.Bitmap.PixelSize, data.SecondaryBitmap.PixelSize);
        var combined = ComputeDrawPlan(data.Bounds, spread.Combined, data.Zoom, data.PanOffsetX, data.PanOffsetY,
            data.FitMode, data.FitOnlyIfOversized, rotationDegrees: 0).DestRect;
        double primaryLeftFraction = data.IsRightToLeft ? 1 - spread.LeftWidthFraction : spread.LeftWidthFraction;
        var (left, right) = SpreadLayoutMath.SplitSpread(combined, primaryLeftFraction);
        DrawPageFrame(context, left, data.FrameColor);
        DrawPageFrame(context, right, data.FrameColor);
    }

    private static void DrawContinuousFramesOnly(ImmediateDrawingContext context, ReaderContinuousVisualData data)
    {
        foreach (var page in data.Pages)
            DrawPageFrame(context, page.Rect, data.FrameColor);
    }

    /// <summary>
    /// Plain <see cref="ImmediateDrawingContext.DrawRectangle"/>, no Skia lease needed -
    /// <see cref="BoxShadows"/> is a standard Avalonia drawing primitive, same mechanism XAML's own
    /// <c>Border.BoxShadow</c> uses - but see <see cref="RenderCore"/>'s own doc comment: it still
    /// throws if called while this same context's lease is already checked out elsewhere, so every
    /// caller of this method must run before that lease is taken, never after.
    /// </summary>
    private static void DrawPageShadow(ImmediateDrawingContext context, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var shadow = new BoxShadow { OffsetX = 0, OffsetY = 6, Blur = 20, Color = Color.FromArgb(140, 0, 0, 0) };
        context.DrawRectangle(TransparentFillBrush, null, rect, 0, 0, new BoxShadows(shadow));
    }

    private static readonly ImmutableSolidColorBrush TransparentFillBrush = new(Colors.Transparent);

    /// <summary>
    /// Draws two bitmaps as one double-page spread (docs/superpowers/specs/2026-08-15-reader-double-
    /// page-spread-design.md §4) - reduces to "one wider virtual page" fed through the same
    /// <see cref="ComputeDrawPlan"/> the solo-page path uses, just with
    /// <see cref="SpreadLayoutMath.ComputeCombinedSize"/>'s combined <see cref="PixelSize"/> standing
    /// in for a single bitmap's own. The resulting <see cref="PageDrawPlan.DestRect"/> is split via
    /// <see cref="SpreadLayoutMath.SplitSpread"/> into the two halves' own placements.
    /// <paramref name="primary"/> (the lower page index) sits on the visual left for
    /// <see cref="Data.Entities.ReadingMode.LeftToRight"/>, the right for
    /// <see cref="Data.Entities.ReadingMode.RightToLeft"/> (spec §4 - same spatial convention
    /// <see cref="PageCanvas"/>'s Left/Right commands already use, not reading-direction-relative
    /// naming). Rotation is inert for spreads (spec's named simplification) - always 0 here.
    /// <paramref name="offset"/>/<paramref name="alpha"/> let <see cref="RenderTransition"/> apply
    /// the same slide offset/crossfade alpha to both halves as one visual unit, same as a solo page's
    /// own transition. The offset is a <see cref="Vector"/> so a vertical page-turn
    /// (<see cref="Data.Entities.ReadingMode.TopToBottom"/>) can slide the spread along Y.
    /// </summary>
    private void RenderSpread(ImmediateDrawingContext context, Rect bounds, Bitmap primary, Bitmap secondary,
        double zoom, double panOffsetX, double panOffsetY, ImageFitMode fitMode, bool fitOnlyIfOversized, bool highQuality,
        bool isRightToLeft, ISkiaSharpApiLease? lease, SKColorFilter? colorFilter, Vector offset, double alpha,
        SKImage? primaryCachedImage = null, SKImage? secondaryCachedImage = null, Func<Bitmap, SKImage>? skImageResolver = null,
        bool bookLook = false, double bookProgress = 0)
    {
        if (alpha <= 0)
        {
            return;
        }

        var primaryPixelSize = primary.PixelSize;
        var secondaryPixelSize = secondary.PixelSize;
        var spreadSize = SpreadLayoutMath.ComputeCombinedSize(primaryPixelSize, secondaryPixelSize);

        var combinedPlan = ComputeDrawPlan(bounds, spreadSize.Combined, zoom, panOffsetX, panOffsetY, fitMode, fitOnlyIfOversized, rotationDegrees: 0);
        var shiftedDestRect = combinedPlan.DestRect.Translate(offset);

        // Shadow (Item 1 §1.5), when this is a paged (non-transition) spread, is drawn by
        // DrawPagedShadowsOnly before RenderCore ever takes a lease - not here. This method also
        // draws a transition's spread side, where a shadow is deliberately never drawn at all.

        // For RTL, the fraction passed to SplitSpread flips too, not just which output rect gets
        // labeled "primary" - SplitSpread always gives its Left result leftWidthFraction's share, so
        // putting primary on the right means the *right* result needs primary's own width share
        // (spreadSize.LeftWidthFraction), which only happens by passing 1-that value as the fraction.
        double primaryLeftFraction = isRightToLeft ? 1 - spreadSize.LeftWidthFraction : spreadSize.LeftWidthFraction;
        var (left, right) = SpreadLayoutMath.SplitSpread(shiftedDestRect, primaryLeftFraction);
        var (primaryRect, secondaryRect) = isRightToLeft ? (right, left) : (left, right);
        var primaryPlan = new PageDrawPlan(primaryRect, primaryRect.Center.X, primaryRect.Center.Y);
        var secondaryPlan = new PageDrawPlan(secondaryRect, secondaryRect.Center.X, secondaryRect.Center.Y);

        DrawBitmap(context, primary, primaryPixelSize, primaryPlan, rotationDegrees: 0, highQuality, lease, colorFilter, alpha, primaryCachedImage, skImageResolver);
        DrawBitmap(context, secondary, secondaryPixelSize, secondaryPlan, rotationDegrees: 0, highQuality, lease, colorFilter, alpha, secondaryCachedImage, skImageResolver);

        if (bookLook && lease is not null)
        {
            DrawBookLook(lease.SkCanvas, left, right, bookProgress, isRightToLeft, alpha);
        }
    }

    /// <summary>
    /// Book look on a spread (Motion v2 reader §7). CE's "Realistic pages": a soft bell-shaped bow of shade at the spine on both pages and a
    /// lighter one at each outer edge (<c>ComicDisplayControl.DrawPageOrnaments</c>, <c>PageRendering.CreatePageBow</c>), plus a thin
    /// outline per page. Then page-stack edges outside each outer edge, thicker on the side with more pages: the read side grows as you go
    /// (left when reading left-to-right, right when right-to-left).
    /// </summary>
    internal static void DrawBookLook(SKCanvas canvas, Rect left, Rect right, double progress, bool isRightToLeft, double alpha = 1)
    {
        if (left.Width <= 0 || right.Width <= 0 || alpha <= 0)
        {
            return;
        }

        byte A(double a) => (byte)Math.Clamp(a * alpha * 255, 0, 255);

        void Bow(float edgeX, float innerX, Rect page, double strength)
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(edgeX, 0), new SKPoint(innerX, 0),
                [new SKColor(0, 0, 0, A(strength)), new SKColor(0, 0, 0, A(strength * 0.35)), SKColors.Transparent], [0f, 0.35f, 1f], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, IsAntialias = true };
            canvas.DrawRect(new SKRect((float)Math.Min(edgeX, innerX), (float)page.Top, (float)Math.Max(edgeX, innerX), (float)page.Bottom), paint);
        }

        // Spine bows (deep) and outer-edge bows (light).
        Bow((float)left.Right, (float)(left.Right - (left.Width * 0.09)), left, 0.42);
        Bow((float)right.Left, (float)(right.Left + (right.Width * 0.09)), right, 0.42);
        Bow((float)left.Left, (float)(left.Left + (left.Width * 0.035)), left, 0.16);
        Bow((float)right.Right, (float)(right.Right - (right.Width * 0.035)), right, 0.16);

        using (var outline = new SKPaint { Color = new SKColor(0, 0, 0, A(0.30)), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true })
        {
            canvas.DrawRect(new SKRect((float)left.Left + 0.5f, (float)left.Top + 0.5f, (float)left.Right - 0.5f, (float)left.Bottom - 0.5f), outline);
            canvas.DrawRect(new SKRect((float)right.Left + 0.5f, (float)right.Top + 0.5f, (float)right.Right - 0.5f, (float)right.Bottom - 0.5f), outline);
        }

        // Page-stack edges: 1-6 sheet edges per side, from the reading progress.
        var (leftSheets, rightSheets) = PageStackSheets(progress, isRightToLeft);
        DrawStack(canvas, left, leftSheets, towardLeft: true, alpha);
        DrawStack(canvas, right, rightSheets, towardLeft: false, alpha);
    }

    /// <summary>How many sheet edges each side of a spread shows for <paramref name="progress"/> through the issue: the side already read grows
    /// from 1 to 6 while the other shrinks from 6 to 1.</summary>
    internal static (int Left, int Right) PageStackSheets(double progress, bool isRightToLeft)
    {
        int read = 1 + (int)Math.Round(Math.Clamp(progress, 0, 1) * 5, MidpointRounding.AwayFromZero);
        int unread = 7 - read;
        return isRightToLeft ? (unread, read) : (read, unread);
    }

    private static void DrawStack(SKCanvas canvas, Rect page, int sheets, bool towardLeft, double alpha)
    {
        using var paper = new SKPaint { Color = new SKColor(232, 228, 218, (byte)(alpha * 255)), IsAntialias = true };
        using var line = new SKPaint { Color = new SKColor(150, 144, 132, (byte)(alpha * 200)), IsAntialias = true };
        for (int i = sheets; i >= 1; i--)
        {
            float x = towardLeft ? (float)(page.Left - (i * 1.6)) : (float)(page.Right + ((i - 1) * 1.6));
            float inset = (float)(i * 0.9);
            var rect = new SKRect(x, (float)page.Top + inset, x + 1.6f, (float)page.Bottom - inset);
            canvas.DrawRect(rect, paper);
            canvas.DrawRect(towardLeft ? new SKRect(rect.Left, rect.Top, rect.Left + 0.6f, rect.Bottom) : new SKRect(rect.Right - 0.6f, rect.Top, rect.Right, rect.Bottom), line);
        }
    }

    /// <summary>
    /// Draws the outgoing and incoming bitmap of an in-flight page turn (spec §3.2), each
    /// independently placed via <see cref="ComputeDrawPlan"/> against its own pixel size.
    /// <see cref="Data.Entities.PageTransitionStyle.Slide"/> offsets each plan's <see cref="PageDrawPlan.DestRect"/>
    /// (and rotation pivot, so a rotated page still rotates around its own current on-screen center
    /// mid-slide) along the main axis via <see cref="PageTransitionMath.SlideOffset"/>;
    /// <see cref="Data.Entities.PageTransitionStyle.Crossfade"/> leaves placement alone and blends
    /// alpha via <see cref="PageTransitionMath.CrossfadeAlpha"/>.
    /// </summary>
    private void RenderTransition(ImmediateDrawingContext context, ReaderPageTransitionData data, double progress,
        SKImage? oldImage, SKImage? newImage, SKImage? oldSecondaryImage, SKImage? newSecondaryImage,
        ISkiaSharpApiLease? lease, SKColorFilter? colorFilter)
    {
        if (data.Bounds.Width <= 0 || data.Bounds.Height <= 0)
        {
            return;
        }

        if (RenderPhysicalTransition(context, data, progress, oldImage, newImage, oldSecondaryImage, newSecondaryImage, lease, colorFilter))
        {
            return;
        }

        if (data.Style == Data.Entities.PageTransitionStyle.Slide)
        {
            bool vertical = data.Direction is PageTransitionDirection.Up or PageTransitionDirection.Down;
            double extent = vertical ? data.Bounds.Height : data.Bounds.Width;
            var (outgoingOffset, incomingOffset) = PageTransitionMath.SlideOffset(data.Direction, progress, extent);
            Vector Slide(double o) => vertical ? new Vector(0, o) : new Vector(o, 0);
            DrawTransitionSide(context, data.OldBitmap, data.OldSecondaryBitmap, oldImage, oldSecondaryImage, data, data.OldIsRightToLeft, Slide(outgoingOffset), alpha: 1.0, lease, colorFilter);
            DrawTransitionSide(context, data.NewBitmap, data.NewSecondaryBitmap, newImage, newSecondaryImage, data, data.IsRightToLeft, Slide(incomingOffset), alpha: 1.0, lease, colorFilter);
            return;
        }

        var (outgoingAlpha, incomingAlpha) = PageTransitionMath.CrossfadeAlpha(progress);
        DrawTransitionSide(context, data.OldBitmap, data.OldSecondaryBitmap, oldImage, oldSecondaryImage, data, data.OldIsRightToLeft, offset: default, outgoingAlpha, lease, colorFilter);
        DrawTransitionSide(context, data.NewBitmap, data.NewSecondaryBitmap, newImage, newSecondaryImage, data, data.IsRightToLeft, offset: default, incomingAlpha, lease, colorFilter);
    }

    /// <summary>
    /// The physical page-turn styles (Motion v2 E, Q30): <see cref="Data.Entities.PageTransitionStyle.Vertical"/> slides on the vertical axis
    /// whatever the reading direction (CE <c>TopDown</c>); <see cref="Data.Entities.PageTransitionStyle.Book"/> moves one page with a spine
    /// shadow (CE <c>Paging</c>); <see cref="Data.Entities.PageTransitionStyle.Curl"/> folds the old page away from its outer corner. Returns
    /// false for a style this method doesn't draw (Slide, Crossfade), or a Curl with no Skia canvas, so the caller falls back.
    /// </summary>
    private bool RenderPhysicalTransition(ImmediateDrawingContext context, ReaderPageTransitionData data, double progress,
        SKImage? oldImage, SKImage? newImage, SKImage? oldSecondaryImage, SKImage? newSecondaryImage,
        ISkiaSharpApiLease? lease, SKColorFilter? colorFilter)
    {
        switch (data.Style)
        {
            case Data.Entities.PageTransitionStyle.Vertical:
            {
                var direction = PageTransitionMath.IsForwardReading(data.Direction, data.IsRightToLeft) ? PageTransitionDirection.Down : PageTransitionDirection.Up;
                var (outgoing, incoming) = PageTransitionMath.SlideOffset(direction, progress, data.Bounds.Height);
                DrawTransitionSide(context, data.OldBitmap, data.OldSecondaryBitmap, oldImage, oldSecondaryImage, data, data.OldIsRightToLeft, new Vector(0, outgoing), 1.0, lease, colorFilter);
                DrawTransitionSide(context, data.NewBitmap, data.NewSecondaryBitmap, newImage, newSecondaryImage, data, data.IsRightToLeft, new Vector(0, incoming), 1.0, lease, colorFilter);
                return true;
            }

            case Data.Entities.PageTransitionStyle.Book:
                RenderBook(context, data, progress, oldImage, newImage, oldSecondaryImage, newSecondaryImage, lease, colorFilter);
                return true;

            // Spread to spread turns one leaf over the spine; a solo page on either side (a cover beside a spread) curls as one sheet.
            case Data.Entities.PageTransitionStyle.Curl when lease is not null && data.OldBitmap is not null && data.NewBitmap is not null
                                                             && data.OldSecondaryBitmap is not null && data.NewSecondaryBitmap is not null:
                RenderSpreadCurl(context, data, progress, oldImage, newImage, oldSecondaryImage, newSecondaryImage, lease, colorFilter);
                return true;

            case Data.Entities.PageTransitionStyle.Curl when lease is not null:
                RenderCurl(context, data, progress, oldImage, newImage, oldSecondaryImage, newSecondaryImage, lease, colorFilter);
                return true;

            default:
                return false;
        }
    }

    private void RenderBook(ImmediateDrawingContext context, ReaderPageTransitionData data, double progress,
        SKImage? oldImage, SKImage? newImage, SKImage? oldSecondaryImage, SKImage? newSecondaryImage,
        ISkiaSharpApiLease? lease, SKColorFilter? colorFilter)
    {
        bool vertical = data.Direction is PageTransitionDirection.Up or PageTransitionDirection.Down;
        double extent = vertical ? data.Bounds.Height : data.Bounds.Width;
        bool forward = PageTransitionMath.IsForwardReading(data.Direction, data.IsRightToLeft);
        var (newMoves, offset) = PageTransitionMath.BookOffset(data.Direction, forward, progress, extent);
        var shift = vertical ? new Vector(0, offset) : new Vector(offset, 0);

        // The still page underneath first, then the moving one over it.
        if (newMoves)
        {
            DrawTransitionSide(context, data.OldBitmap, data.OldSecondaryBitmap, oldImage, oldSecondaryImage, data, data.OldIsRightToLeft, default, 1.0, lease, colorFilter);
        }
        else
        {
            DrawTransitionSide(context, data.NewBitmap, data.NewSecondaryBitmap, newImage, newSecondaryImage, data, data.IsRightToLeft, default, 1.0, lease, colorFilter);
        }

        var movingRect = (newMoves ? SideRect(data, data.NewBitmap, data.NewSecondaryBitmap) : SideRect(data, data.OldBitmap, data.OldSecondaryBitmap)).Translate(shift);

        // Spine shadow: cast onto the still page just beyond the moving page's edge, strongest mid-turn.
        if (lease is not null && movingRect.Width > 0)
        {
            double strength = Math.Sin(Math.PI * PageTransitionMath.Smooth(progress)) * 0.45;
            const float shadowSize = 28f;
            SKPoint from, to;
            SKRect band;
            if (vertical)
            {
                bool below = offset >= 0;
                float edge = (float)(below ? movingRect.Top : movingRect.Bottom);
                from = new SKPoint(0, edge);
                to = new SKPoint(0, below ? edge - shadowSize : edge + shadowSize);
                band = below ? new SKRect((float)movingRect.Left, edge - shadowSize, (float)movingRect.Right, edge) : new SKRect((float)movingRect.Left, edge, (float)movingRect.Right, edge + shadowSize);
            }
            else
            {
                bool right = offset >= 0;
                float edge = (float)(right ? movingRect.Left : movingRect.Right);
                from = new SKPoint(edge, 0);
                to = new SKPoint(right ? edge - shadowSize : edge + shadowSize, 0);
                band = right ? new SKRect(edge - shadowSize, (float)movingRect.Top, edge, (float)movingRect.Bottom) : new SKRect(edge, (float)movingRect.Top, edge + shadowSize, (float)movingRect.Bottom);
            }

            using var shader = SKShader.CreateLinearGradient(from, to,
                [new SKColor(0, 0, 0, (byte)(strength * 255)), SKColors.Transparent], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, IsAntialias = true };
            lease.SkCanvas.DrawRect(band, paint);
        }

        if (newMoves)
        {
            DrawTransitionSide(context, data.NewBitmap, data.NewSecondaryBitmap, newImage, newSecondaryImage, data, data.IsRightToLeft, shift, 1.0, lease, colorFilter);
        }
        else
        {
            DrawTransitionSide(context, data.OldBitmap, data.OldSecondaryBitmap, oldImage, oldSecondaryImage, data, data.OldIsRightToLeft, shift, 1.0, lease, colorFilter);
        }
    }

    /// <summary>
    /// The corner curl. Reading forward the old page folds away and the new one is uncovered; reading backward the curl plays in reverse
    /// with the pages swapped, so the previous page folds back down over the current one. The turning page is clipped to its unturned
    /// side of the fold; the page beneath gets a shadow along the fold; the turned-over part lands mirrored across the fold as the paper's
    /// back, with a soft shadow under it.
    /// </summary>
    private void RenderCurl(ImmediateDrawingContext context, ReaderPageTransitionData data, double progress,
        SKImage? oldImage, SKImage? newImage, SKImage? oldSecondaryImage, SKImage? newSecondaryImage,
        ISkiaSharpApiLease lease, SKColorFilter? colorFilter)
    {
        bool forward = PageTransitionMath.IsForwardReading(data.Direction, data.IsRightToLeft);
        double t = forward ? progress : 1 - progress;
        var canvas = lease.SkCanvas;

        void DrawTurning() => (forward ? (Action)(() => DrawTransitionSide(context, data.OldBitmap, data.OldSecondaryBitmap, oldImage, oldSecondaryImage, data, data.OldIsRightToLeft, default, 1.0, lease, colorFilter))
            : () => DrawTransitionSide(context, data.NewBitmap, data.NewSecondaryBitmap, newImage, newSecondaryImage, data, data.IsRightToLeft, default, 1.0, lease, colorFilter))();
        void DrawUnder() => (forward ? (Action)(() => DrawTransitionSide(context, data.NewBitmap, data.NewSecondaryBitmap, newImage, newSecondaryImage, data, data.IsRightToLeft, default, 1.0, lease, colorFilter))
            : () => DrawTransitionSide(context, data.OldBitmap, data.OldSecondaryBitmap, oldImage, oldSecondaryImage, data, data.OldIsRightToLeft, default, 1.0, lease, colorFilter))();

        var page = forward ? SideRect(data, data.OldBitmap, data.OldSecondaryBitmap) : SideRect(data, data.NewBitmap, data.NewSecondaryBitmap);
        var (on, normal, active) = PageTransitionMath.CurlFold(page, t, mirrored: data.IsRightToLeft);

        DrawUnder();
        if (!active || page.Width <= 0)
        {
            if (t < 0.5)
            {
                DrawTurning();
            }

            return;
        }

        var corners = PageTransitionMath.Corners(page);
        var unturned = PageTransitionMath.ClipToSide(corners, on, normal, normalSide: false);
        var turned = PageTransitionMath.ClipToSide(corners, on, normal, normalSide: true);
        var flap = turned.ConvertAll(p => PageTransitionMath.Reflect(p, on, normal));

        // 1. The part of the turning page still lying flat.
        if (unturned.Count >= 3)
        {
            using var clip = ToPath(unturned);
            int save = canvas.Save();
            canvas.ClipPath(clip, antialias: true);
            DrawTurning();
            canvas.RestoreToCount(save);
        }

        // 2. The shadow the lifting page casts on the page beneath.
        double lift = Math.Sin(Math.PI * PageTransitionMath.Smooth(t));
        DrawCastShadow(canvas, turned, on, normal, lift, page);

        // 3. The folded-over paper back, with a soft shadow beneath it and curvature shading.
        if (flap.Count >= 3)
        {
            using var flapPath = ToPath(flap);
            using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(50 + (40 * lift))), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(4 + (6 * lift))) })
            {
                int save = canvas.Save();
                canvas.Translate((float)(-normal.X * 3), (float)(-normal.Y * 3 + 2));
                canvas.DrawPath(flapPath, shadow);
                canvas.RestoreToCount(save);
            }

            using (var paper = new SKPaint { Color = new SKColor(240, 236, 228), IsAntialias = true })
            {
                canvas.DrawPath(flapPath, paper);
            }

            DrawFlapCurvature(canvas, flapPath, on, normal, page.Width);
        }
    }


    /// <summary>
    /// The curl on a double-page spread: like a book, only one leaf turns. Reading forward from spread A to spread B, A's later page (the
    /// right one left-to-right, the left one right-to-left - always the spread's secondary page) lifts at its outer corner and folds over the
    /// spine. Beneath it B's secondary page is uncovered; the leaf's back is B's primary page, landing over A's primary as the turn completes.
    /// Reading backward the same turn plays in reverse with the spreads swapped, so the earlier spread's leaf folds back down.
    /// </summary>
    private void RenderSpreadCurl(ImmediateDrawingContext context, ReaderPageTransitionData data, double progress,
        SKImage? oldImage, SKImage? newImage, SKImage? oldSecondaryImage, SKImage? newSecondaryImage,
        ISkiaSharpApiLease lease, SKColorFilter? colorFilter)
    {
        bool forward = PageTransitionMath.IsForwardReading(data.Direction, data.IsRightToLeft);
        double t = forward ? progress : 1 - progress;
        var canvas = lease.SkCanvas;

        // A = the earlier spread, B = the later one.
        var (aPrimary, aSecondary, aPrimaryImage, aSecondaryImage, aRtl) = forward
            ? (data.OldBitmap!, data.OldSecondaryBitmap!, oldImage, oldSecondaryImage, data.OldIsRightToLeft)
            : (data.NewBitmap!, data.NewSecondaryBitmap!, newImage, newSecondaryImage, data.IsRightToLeft);
        var (bPrimary, bSecondary, bPrimaryImage, bSecondaryImage, bRtl) = forward
            ? (data.NewBitmap!, data.NewSecondaryBitmap!, newImage, newSecondaryImage, data.IsRightToLeft)
            : (data.OldBitmap!, data.OldSecondaryBitmap!, oldImage, oldSecondaryImage, data.OldIsRightToLeft);

        var (aPrimaryRect, aSecondaryRect) = SpreadHalves(data, aPrimary, aSecondary, aRtl);
        var (bPrimaryRect, bSecondaryRect) = SpreadHalves(data, bPrimary, bSecondary, bRtl);

        void Draw(Bitmap bitmap, SKImage? image, Rect rect) =>
            DrawBitmap(context, bitmap, bitmap.PixelSize, new PageDrawPlan(rect, rect.Center.X, rect.Center.Y), rotationDegrees: 0,
                data.HighQuality, lease, colorFilter, 1.0, image);

        // The page the leaf will cover stays put, and the next page waits beneath the leaf - with the book look, as when still.
        Draw(aPrimary, aPrimaryImage, aPrimaryRect);
        Draw(bSecondary, bSecondaryImage, bSecondaryRect);
        if (data.BookLook)
        {
            var (leftPage, rightPage) = aRtl ? (bSecondaryRect, aPrimaryRect) : (aPrimaryRect, bSecondaryRect);
            DrawBookLook(canvas, leftPage, rightPage, data.BookProgress, aRtl);
        }

        var leaf = aSecondaryRect;
        var (on, normal, active) = PageTransitionMath.CurlFold(leaf, t, mirrored: aRtl);
        if (!active || leaf.Width <= 0)
        {
            if (t < 0.5)
            {
                Draw(aSecondary, aSecondaryImage, leaf);
            }
            else
            {
                Draw(bPrimary, bPrimaryImage, bPrimaryRect);
            }

            return;
        }

        var corners = PageTransitionMath.Corners(leaf);
        var flat = PageTransitionMath.ClipToSide(corners, on, normal, normalSide: false);
        var turned = PageTransitionMath.ClipToSide(corners, on, normal, normalSide: true);
        var flap = turned.ConvertAll(p => PageTransitionMath.Reflect(p, on, normal));

        // 1. The part of the leaf still lying flat.
        if (flat.Count >= 3)
        {
            using var clip = ToPath(flat);
            int save = canvas.Save();
            canvas.ClipPath(clip, antialias: true);
            Draw(aSecondary, aSecondaryImage, leaf);
            canvas.RestoreToCount(save);
        }

        // 2. The lifted leaf darkens the spine on both pages, and casts its shadow on the page it uncovers.
        double lift = Math.Sin(Math.PI * PageTransitionMath.Smooth(t));
        double spine = aRtl ? aPrimaryRect.Left : aPrimaryRect.Right;
        DrawSpineShadow(canvas, spine, aPrimaryRect.Union(bSecondaryRect), lift);
        DrawCastShadow(canvas, turned, on, normal, lift, leaf);

        // 3. The leaf's back - the next spread's first page - folded over the spine, with a soft shadow beneath and a curve shade near the
        //    fold. Paper fills any part of the flap the page itself doesn't reach (pages of different sizes).
        if (flap.Count >= 3)
        {
            using var flapPath = ToPath(flap);
            using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 70), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6) })
            {
                int save = canvas.Save();
                canvas.Translate((float)(-normal.X * 3), (float)(-normal.Y * 3 + 2));
                canvas.DrawPath(flapPath, shadow);
                canvas.RestoreToCount(save);
            }

            int flapSave = canvas.Save();
            canvas.ClipPath(flapPath, antialias: true);
            using (var paper = new SKPaint { Color = new SKColor(236, 232, 223), IsAntialias = true })
            {
                canvas.DrawPath(flapPath, paper);
            }

            Draw(bPrimary, bPrimaryImage, bPrimaryRect);
            canvas.RestoreToCount(flapSave);
            DrawFlapCurvature(canvas, flapPath, on, normal, leaf.Width);
        }
    }

    /// <summary>A spread's two page rects on screen (unshifted), placed exactly as <see cref="RenderSpread"/> places them.</summary>
    private static (Rect Primary, Rect Secondary) SpreadHalves(ReaderPageTransitionData data, Bitmap primary, Bitmap secondary, bool isRightToLeft)
    {
        var spreadSize = SpreadLayoutMath.ComputeCombinedSize(primary.PixelSize, secondary.PixelSize);
        var combined = ComputeDrawPlan(data.Bounds, spreadSize.Combined, data.Zoom, data.PanOffsetX, data.PanOffsetY, data.FitMode,
            data.FitOnlyIfOversized, rotationDegrees: 0).DestRect;
        double primaryLeftFraction = isRightToLeft ? 1 - spreadSize.LeftWidthFraction : spreadSize.LeftWidthFraction;
        var (left, right) = SpreadLayoutMath.SplitSpread(combined, primaryLeftFraction);
        return isRightToLeft ? (right, left) : (left, right);
    }

    /// <summary>
    /// Curl polish (Motion v2 reader §7): the shadow the lifting page casts on the page beneath, wider and darker the higher it lifts.
    /// </summary>
    private static void DrawCastShadow(SKCanvas canvas, IReadOnlyList<Point> turned, Point on, Vector normal, double lift, Rect clipRect)
    {
        if (turned.Count < 3)
        {
            return;
        }

        using var clip = ToPath(turned);
        int save = canvas.Save();
        canvas.ClipPath(clip, antialias: true);
        double reach = 24 + (70 * lift);
        var from = new SKPoint((float)on.X, (float)on.Y);
        var to = new SKPoint((float)(on.X + (normal.X * reach)), (float)(on.Y + (normal.Y * reach)));
        using var shader = SKShader.CreateLinearGradient(from, to,
            [new SKColor(0, 0, 0, (byte)(70 + (90 * lift))), new SKColor(0, 0, 0, (byte)(25 + (30 * lift))), SKColors.Transparent], [0f, 0.4f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRect(ToSk(clipRect), paint);
        canvas.RestoreToCount(save);
    }

    /// <summary>
    /// Curl polish: the turned-over paper reads as curved, not cut - a dark crease right at the fold, a highlight where the curve faces the
    /// light, easing back to flat - and a fine light line along the flap's edges. Drawn over whatever fills the flap.
    /// </summary>
    private static void DrawFlapCurvature(SKCanvas canvas, SKPath flapPath, Point on, Vector normal, double leafWidth)
    {
        int save = canvas.Save();
        canvas.ClipPath(flapPath, antialias: true);
        var from = new SKPoint((float)on.X, (float)on.Y);
        var to = new SKPoint((float)(on.X - (normal.X * leafWidth * 0.5)), (float)(on.Y - (normal.Y * leafWidth * 0.5)));
        using var shader = SKShader.CreateLinearGradient(from, to,
            [new SKColor(0, 0, 0, 95), new SKColor(255, 255, 255, 70), new SKColor(255, 255, 255, 0), new SKColor(0, 0, 0, 22)],
            [0f, 0.12f, 0.45f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawPath(flapPath, paint);
        canvas.RestoreToCount(save);

        using var edge = new SKPaint { Color = new SKColor(255, 255, 255, 120), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawPath(flapPath, edge);
    }

    /// <summary>On a spread, the lifted leaf darkens the spine on both pages; strongest mid-turn.</summary>
    private static void DrawSpineShadow(SKCanvas canvas, double spineX, Rect span, double lift)
    {
        if (lift <= 0.01)
        {
            return;
        }

        float half = (float)(10 + (34 * lift));
        using var shader = SKShader.CreateLinearGradient(new SKPoint((float)spineX - half, 0), new SKPoint((float)spineX + half, 0),
            [SKColors.Transparent, new SKColor(0, 0, 0, (byte)(110 * lift)), SKColors.Transparent], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRect(new SKRect((float)spineX - half, (float)span.Top, (float)spineX + half, (float)span.Bottom), paint);
    }

    /// <summary>Where one side (solo page or spread) of a turn sits on screen, unshifted.</summary>
    private static Rect SideRect(ReaderPageTransitionData data, Bitmap? primary, Bitmap? secondary)
    {
        if (primary is null)
        {
            return default;
        }

        var size = secondary is null ? primary.PixelSize : SpreadLayoutMath.ComputeCombinedSize(primary.PixelSize, secondary.PixelSize).Combined;
        return ComputeDrawPlan(data.Bounds, size, data.Zoom, data.PanOffsetX, data.PanOffsetY, data.FitMode, data.FitOnlyIfOversized,
            secondary is null ? data.RotationDegrees : 0).DestRect;
    }

    private static SKPath ToPath(IReadOnlyList<Point> polygon)
    {
        var path = new SKPath();
        path.MoveTo((float)polygon[0].X, (float)polygon[0].Y);
        for (int i = 1; i < polygon.Count; i++)
        {
            path.LineTo((float)polygon[i].X, (float)polygon[i].Y);
        }

        path.Close();
        return path;
    }

    private static SKRect ToSk(Rect r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);

    /// <summary>
    /// Draws one side (old or new) of an in-flight turn (docs/superpowers/specs/2026-08-15-reader-
    /// double-page-spread-design.md §5) - solo when <paramref name="secondaryBitmap"/> is null (same
    /// as before spreads existed), a spread via <see cref="RenderSpread"/> otherwise, both sharing the
    /// same <paramref name="offset"/>/<paramref name="alpha"/> so a paired side moves/fades as one
    /// visual unit rather than its two halves animating independently. <paramref name="isRightToLeft"/>
    /// is a parameter rather than read from <paramref name="data"/> directly so the caller can pass
    /// <see cref="ReaderPageTransitionData.OldIsRightToLeft"/> for the old side and
    /// <see cref="ReaderPageTransitionData.IsRightToLeft"/> for the new one. <paramref name="cachedImage"/>/
    /// <paramref name="secondaryCachedImage"/> are the transition-scoped, converted-once
    /// <see cref="SKImage"/>s (<see cref="_transitionOldImage"/> etc.) - only non-null for a Crossfade
    /// turn, since Slide never needs the lease path unless a color filter is separately active.
    /// </summary>
    private void DrawTransitionSide(ImmediateDrawingContext context, Bitmap? bitmap, Bitmap? secondaryBitmap,
        SKImage? cachedImage, SKImage? secondaryCachedImage, ReaderPageTransitionData data, bool isRightToLeft, Vector offset, double alpha,
        ISkiaSharpApiLease? lease, SKColorFilter? colorFilter)
    {
        if (bitmap is null || alpha <= 0)
        {
            return;
        }

        if (secondaryBitmap is null)
        {
            var pixelSize = bitmap.PixelSize;
            var plan = ComputeDrawPlan(data.Bounds, pixelSize, data.Zoom, data.PanOffsetX, data.PanOffsetY, data.FitMode, data.FitOnlyIfOversized, data.RotationDegrees);
            var offsetPlan = plan with { DestRect = plan.DestRect.Translate(offset), CenterX = plan.CenterX + offset.X, CenterY = plan.CenterY + offset.Y };
            DrawBitmap(context, bitmap, pixelSize, offsetPlan, data.RotationDegrees, data.HighQuality, lease, colorFilter, alpha, cachedImage);
            return;
        }

        RenderSpread(context, data.Bounds, bitmap, secondaryBitmap, data.Zoom, data.PanOffsetX, data.PanOffsetY,
            data.FitMode, data.FitOnlyIfOversized, data.HighQuality, isRightToLeft, lease, colorFilter, offset, alpha,
            primaryCachedImage: cachedImage, secondaryCachedImage: secondaryCachedImage,
            bookLook: data.BookLook, bookProgress: data.BookProgress);
    }

    // Decode reveal (Motion v2 E, Q37): pages seen on screen as a gap (still decoding) fade in once their bitmap arrives; a page decoded
    // before it scrolled in just appears, and none fades twice. Keyed by position in the page list, reset when the document changes.
    private readonly HashSet<int> _continuousGaps = new();
    private readonly Dictionary<int, TimeSpan> _continuousReveals = new();
    private int _continuousPageCount = -1;

    /// <summary>
    /// Each <see cref="ContinuousPageEntry.Rect"/> is already the final on-screen placement -
    /// <see cref="ReaderLayoutModel.ComputeContinuousLayout"/> already applied fit/zoom/pan/scroll,
    /// so this is a straight scale-and-draw per visible page, no per-page fit math. No rotation
    /// support in continuous mode this pass (spec §9's named scope). See <see cref="OnRender"/>'s own
    /// doc comment for why the <paramref name="lease"/>/<paramref name="colorFilter"/> branch can't
    /// just wrap the existing <c>context.DrawBitmap</c> path.
    /// </summary>
    private void RenderContinuous(ImmediateDrawingContext context, ReaderContinuousVisualData data, ISkiaSharpApiLease? lease, SKColorFilter? colorFilter)
    {
        var mode = data.HighQuality ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.LowQuality;
        var viewport = new Rect(data.Bounds);

        if (data.Pages.Count != _continuousPageCount)
        {
            _continuousPageCount = data.Pages.Count;
            _continuousGaps.Clear();
            _continuousReveals.Clear();
        }

        var revealFade = Theming.Motion.MotionPolicy.Resolve(Theming.Motion.MotionRole.Replace).Fade;
        for (int index = 0; index < data.Pages.Count; index++)
        {
            var entry = data.Pages[index];
            if (entry.Rect.Width <= 0 || entry.Rect.Height <= 0)
            {
                continue;
            }

            // Pages (or, for a strip, its whole span) entirely outside the viewport (the
            // virtualization fringe, kept decoded for smoothness but not on-screen right now) don't
            // need a draw call at all.
            if (!entry.Rect.Intersects(viewport))
            {
                continue;
            }

            if (entry.Bands is { } bands)
            {
                // Webtoon strip (design §4.3) - each band slot draws (or, for a still-decoding
                // slot, is simply skipped, drawing a gap) independently, at its own already-
                // continuous-mapped Rect (§4.1.2 - never re-derived from a neighboring band's
                // rounded rect, which is what would create a seam).
                foreach (var band in bands)
                {
                    if (band.Bitmap is null || band.Rect.Width <= 0 || band.Rect.Height <= 0 || !band.Rect.Intersects(viewport))
                    {
                        continue;
                    }
                    DrawOneContinuousBitmap(context, band.Bitmap, band.Rect, mode, lease, colorFilter);
                }
                continue;
            }

            if (entry.Bitmap is null)
            {
                if (revealFade > TimeSpan.Zero)
                {
                    _continuousGaps.Add(index);
                }

                continue;
            }

            double alpha = 1;
            if (_continuousGaps.Remove(index))
            {
                _continuousReveals[index] = CompositionNow;
            }

            if (_continuousReveals.TryGetValue(index, out var revealStart))
            {
                double t = revealFade > TimeSpan.Zero ? (CompositionNow - revealStart).TotalMilliseconds / revealFade.TotalMilliseconds : 1;
                if (t >= 1)
                {
                    _continuousReveals.Remove(index);
                }
                else
                {
                    alpha = 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
                }
            }

            DrawOneContinuousBitmap(context, entry.Bitmap, entry.Rect, mode, lease, colorFilter, alpha);
        }

        if (_continuousReveals.Count > 0)
        {
            RegisterForNextAnimationFrameUpdate();
        }

        EvictStaleContinuousScales(data.Pages);
    }

    /// <summary>One page's or one band's actual draw call - split out of <see cref="RenderContinuous"/> so the whole-page and per-band paths (design §4.3) share identical draw logic rather than two copies drifting apart.</summary>
    private void DrawOneContinuousBitmap(ImmediateDrawingContext context, Bitmap bitmap, Rect rect, BitmapInterpolationMode mode, ISkiaSharpApiLease? lease, SKColorFilter? colorFilter, double alpha = 1)
    {
        if (lease is not null && (NeedsPaint(colorFilter) || alpha < 1))
        {
            var pixelSize = bitmap.PixelSize;
            var skImage = GetOrCreateSkImage(bitmap); // cached across frames (§10), not per-frame
            using var paint = new SKPaint { ColorFilter = colorFilter, ImageFilter = _sharpenFilter, IsAntialias = true, Color = new SKColor(255, 255, 255, (byte)(Math.Clamp(alpha, 0, 1) * 255)) };
            var sourceRect = new SKRect(0, 0, pixelSize.Width, pixelSize.Height);
            var destRectSk = new SKRect((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));
            lease.SkCanvas.DrawImage(skImage, sourceRect, destRectSk, SamplingFor(mode == BitmapInterpolationMode.HighQuality, rect.Width, pixelSize.Width), paint);
            return;
        }

        // Was: CreateScaledBitmap(rect size) + DrawBitmap every compose pass - a full Skia resample
        // and a fresh bitmap allocation per visible page per frame, sustained through every scroll,
        // which is what starved the render thread and showed as tearing/judder. Now: hand the
        // compositor the source bitmap for a 1:1 / upscale draw (the default fit-width zoom), or a
        // cached downscale that survives across frames (see _continuousScaledCache).
        var drawBitmap = ResolveContinuousDrawBitmap(bitmap, rect, mode);
        context.DrawBitmap(drawBitmap, new Rect(drawBitmap.Size), rect);
    }

    /// <summary>
    /// The bitmap to draw for one continuous-mode page: the source bitmap itself whenever the
    /// on-screen rect is at or above its native size (a 1:1 blit or a GPU upscale - nothing a CPU
    /// pre-pass would improve), or a cached CPU downscale when the page is drawn smaller than native
    /// so a held zoomed-out view keeps the same <see cref="BitmapInterpolationMode.HighQuality"/>
    /// resample it had before without re-running it every compose frame. See
    /// <see cref="_continuousScaledCache"/>.
    /// </summary>
    private Bitmap ResolveContinuousDrawBitmap(Bitmap source, Rect destRect, BitmapInterpolationMode mode)
    {
        var native = source.PixelSize;
        int targetWidth = Math.Max(1, (int)Math.Round(destRect.Width));
        int targetHeight = Math.Max(1, (int)Math.Round(destRect.Height));

        // 1px rounding slack so the default fit-width zoom (target == native) always takes the
        // zero-copy path rather than resampling to a near-identical size.
        if (targetWidth >= native.Width - 1 || targetHeight >= native.Height - 1)
        {
            if (_continuousScaledCache.Count != 0 && _continuousScaledCache.Remove(source, out var obsolete))
            {
                obsolete.Scaled.Dispose();
            }

            return source;
        }

        var target = new PixelSize(targetWidth, targetHeight);
        if (_continuousScaledCache.TryGetValue(source, out var cached))
        {
            if (cached.Size == target)
            {
                return cached.Scaled;
            }

            cached.Scaled.Dispose();
        }

        var scaled = source.CreateScaledBitmap(target, mode);
        _continuousScaledCache[source] = (target, scaled);
        return scaled;
    }

    /// <summary>Whether <paramref name="bitmap"/> is still referenced by this frame's page list - either as a whole page's own <see cref="ContinuousPageEntry.Bitmap"/>, or as one of a strip's <see cref="ContinuousPageEntry.Bands"/> (design §4.3).</summary>
    private static bool IsBitmapLive(Bitmap bitmap, IReadOnlyList<ContinuousPageEntry> pages)
    {
        foreach (var entry in pages)
        {
            if (ReferenceEquals(entry.Bitmap, bitmap))
            {
                return true;
            }
            if (entry.Bands is { } bands)
            {
                foreach (var band in bands)
                {
                    if (ReferenceEquals(band.Bitmap, bitmap))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>Drops (and disposes) any cached downscale whose source page has left the visible set - the counterpart to <see cref="ResolveContinuousDrawBitmap"/> populating it, so the cache tracks the virtualization window rather than growing without bound.</summary>
    private void EvictStaleContinuousScales(IReadOnlyList<ContinuousPageEntry> pages)
    {
        if (_continuousScaledCache.Count == 0 && _skImageCache.Count == 0)
        {
            return;
        }

        List<Bitmap>? stale = null;
        foreach (var key in _continuousScaledCache.Keys)
        {
            if (!IsBitmapLive(key, pages))
            {
                (stale ??= new List<Bitmap>()).Add(key);
            }
        }

        // SKImage cache (§10) tracks the same visible set.
        if (_skImageCache.Count != 0)
        {
            foreach (var key in _skImageCache.Keys)
            {
                if (!IsBitmapLive(key, pages))
                {
                    (stale ??= new List<Bitmap>()).Add(key);
                }
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var key in stale)
        {
            if (_continuousScaledCache.Remove(key, out var removed))
            {
                removed.Scaled.Dispose();
            }
            if (_skImageCache.Remove(key, out var removedImage))
            {
                removedImage.Dispose();
            }
        }
    }
}
