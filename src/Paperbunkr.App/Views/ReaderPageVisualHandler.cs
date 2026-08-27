using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    bool IsRightToLeft = false);

/// <summary>One page's already-computed on-screen placement (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §2/§4) - <see cref="Rect"/> comes straight from <see cref="ReaderLayoutModel.ComputeContinuousLayout"/>, already in viewport space, so the handler just scales-and-draws rather than repeating fit/pan math per page.</summary>
internal readonly record struct ContinuousPageEntry(Rect Rect, Bitmap? Bitmap);

/// <summary>Continuous mode's per-frame draw data - unlike <see cref="ReaderPageVisualData"/>'s single bitmap, a whole page list, each with its own placement. No fit-mode/rotation fields - continuous mode has neither (spec §5/§9's named scope, rotation isn't part of this pass for continuous mode).</summary>
internal sealed record ReaderContinuousVisualData(Size Bounds, IReadOnlyList<ContinuousPageEntry> Pages, bool HighQuality);

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
    bool OldIsRightToLeft = false);

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
internal sealed record AdjustmentVisualData(double Brightness, double Contrast, double Saturation, double Gamma)
{
    public static readonly AdjustmentVisualData None = new(0, 0, 0, 0);
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
                _pagedData = paged;
                _continuousData = null;
                _transitionData = null;
                _transitionStart = null;
                DisposeTransitionImages();
                Invalidate();
                break;
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
                _transitionData = transition;
                _continuousData = null;
                // CompositionNow, not a UI-thread clock - this handler and OnAnimationFrameUpdate
                // both run on the compositor thread (spec §3.2), so progress is measured against the
                // same clock throughout the animation.
                _transitionStart = CompositionNow;

                // Converted once here, not per-frame - see _transitionOldImage's own doc comment for
                // why. Only Crossfade needs these at all (Slide never takes DrawBitmap's lease path
                // when no color filter is active).
                if (transition.Style == Data.Entities.PageTransitionStyle.Crossfade)
                {
                    _transitionOldImage = transition.OldBitmap is { } oldBitmap ? SkiaBitmapConverter.ToSkImage(oldBitmap) : null;
                    _transitionNewImage = transition.NewBitmap is { } newBitmap ? SkiaBitmapConverter.ToSkImage(newBitmap) : null;
                    _transitionOldSecondaryImage = transition.OldSecondaryBitmap is { } oldSecondary ? SkiaBitmapConverter.ToSkImage(oldSecondary) : null;
                    _transitionNewSecondaryImage = transition.NewSecondaryBitmap is { } newSecondary ? SkiaBitmapConverter.ToSkImage(newSecondary) : null;
                }

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
        var colorFilter = GetColorFilter();

        // A crossfade needs the leased-canvas/SKPaint path regardless of whether a brightness/
        // contrast colorFilter is active - plain ImmediateDrawingContext.DrawBitmap has no opacity
        // overload (confirmed via reflection against the actual Avalonia 12.1.1 assembly), so alpha
        // blending between the outgoing/incoming bitmap is only possible through SKPaint.Color's
        // alpha channel.
        bool needsLease = colorFilter is not null || _transitionData is { Style: Data.Entities.PageTransitionStyle.Crossfade };
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
        if (ImageAdjustmentMath.IsIdentity(_adjustmentData.Brightness, _adjustmentData.Contrast, _adjustmentData.Saturation, _adjustmentData.Gamma))
        {
            _cachedColorFilter = null;
            _cachedFor = _adjustmentData;
            return null;
        }

        if (_cachedColorFilter is not null && _cachedFor == _adjustmentData)
        {
            return _cachedColorFilter;
        }

        var matrix = ImageAdjustmentMath.CreateColorMatrix(_adjustmentData.Brightness, _adjustmentData.Contrast, _adjustmentData.Saturation);
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
    private static void DrawBitmap(ImmediateDrawingContext context, Bitmap bitmap, PixelSize pixelSize, PageDrawPlan plan, int rotationDegrees, bool highQuality, ISkiaSharpApiLease? lease, SKColorFilter? colorFilter, double alpha, SKImage? cachedImage = null)
    {
        if (lease is not null && (colorFilter is not null || alpha < 1.0))
        {
            SKImage skImage = cachedImage ?? SkiaBitmapConverter.ToSkImage(bitmap);
            try
            {
                using var paint = new SKPaint { ColorFilter = colorFilter, IsAntialias = true, Color = new SKColor(255, 255, 255, (byte)(Math.Clamp(alpha, 0, 1) * 255)) };
                var sourceRect = new SKRect(0, 0, pixelSize.Width, pixelSize.Height);
                var destRectSk = new SKRect((float)plan.DestRect.X, (float)plan.DestRect.Y, (float)(plan.DestRect.X + plan.DestRect.Width), (float)(plan.DestRect.Y + plan.DestRect.Height));

                if (rotationDegrees == 0)
                {
                    lease.SkCanvas.DrawImage(skImage, sourceRect, destRectSk, paint);
                    return;
                }

                int saveCount = lease.SkCanvas.Save();
                lease.SkCanvas.RotateDegrees(rotationDegrees, (float)plan.CenterX, (float)plan.CenterY);
                lease.SkCanvas.DrawImage(skImage, sourceRect, destRectSk, paint);
                lease.SkCanvas.RestoreToCount(saveCount);
                return;
            }
            finally
            {
                if (cachedImage is null)
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

    private static void RenderPaged(ImmediateDrawingContext context, ReaderPageVisualData data, ISkiaSharpApiLease? lease, SKColorFilter? colorFilter)
    {
        if (data.Bitmap is null || data.Bounds.Width <= 0 || data.Bounds.Height <= 0)
        {
            return;
        }

        if (data.SecondaryBitmap is null)
        {
            var pixelSize = data.Bitmap.PixelSize;
            var plan = ComputeDrawPlan(data.Bounds, pixelSize, data.Zoom, data.PanOffsetX, data.PanOffsetY, data.FitMode, data.FitOnlyIfOversized, data.RotationDegrees);
            DrawBitmap(context, data.Bitmap, pixelSize, plan, data.RotationDegrees, data.HighQuality, lease, colorFilter, alpha: 1.0);
            return;
        }

        RenderSpread(context, data.Bounds, data.Bitmap, data.SecondaryBitmap, data.Zoom, data.PanOffsetX, data.PanOffsetY,
            data.FitMode, data.FitOnlyIfOversized, data.HighQuality, data.IsRightToLeft, lease, colorFilter, offset: default, alpha: 1.0);
    }

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
    private static void RenderSpread(ImmediateDrawingContext context, Rect bounds, Bitmap primary, Bitmap secondary,
        double zoom, double panOffsetX, double panOffsetY, ImageFitMode fitMode, bool fitOnlyIfOversized, bool highQuality,
        bool isRightToLeft, ISkiaSharpApiLease? lease, SKColorFilter? colorFilter, Vector offset, double alpha,
        SKImage? primaryCachedImage = null, SKImage? secondaryCachedImage = null)
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

        // For RTL, the fraction passed to SplitSpread flips too, not just which output rect gets
        // labeled "primary" - SplitSpread always gives its Left result leftWidthFraction's share, so
        // putting primary on the right means the *right* result needs primary's own width share
        // (spreadSize.LeftWidthFraction), which only happens by passing 1-that value as the fraction.
        double primaryLeftFraction = isRightToLeft ? 1 - spreadSize.LeftWidthFraction : spreadSize.LeftWidthFraction;
        var (left, right) = SpreadLayoutMath.SplitSpread(shiftedDestRect, primaryLeftFraction);
        var (primaryRect, secondaryRect) = isRightToLeft ? (right, left) : (left, right);
        var primaryPlan = new PageDrawPlan(primaryRect, primaryRect.Center.X, primaryRect.Center.Y);
        var secondaryPlan = new PageDrawPlan(secondaryRect, secondaryRect.Center.X, secondaryRect.Center.Y);

        DrawBitmap(context, primary, primaryPixelSize, primaryPlan, rotationDegrees: 0, highQuality, lease, colorFilter, alpha, primaryCachedImage);
        DrawBitmap(context, secondary, secondaryPixelSize, secondaryPlan, rotationDegrees: 0, highQuality, lease, colorFilter, alpha, secondaryCachedImage);
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
    private static void RenderTransition(ImmediateDrawingContext context, ReaderPageTransitionData data, double progress,
        SKImage? oldImage, SKImage? newImage, SKImage? oldSecondaryImage, SKImage? newSecondaryImage,
        ISkiaSharpApiLease? lease, SKColorFilter? colorFilter)
    {
        if (data.Bounds.Width <= 0 || data.Bounds.Height <= 0)
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
    private static void DrawTransitionSide(ImmediateDrawingContext context, Bitmap? bitmap, Bitmap? secondaryBitmap,
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
            data.FitMode, data.FitOnlyIfOversized, data.HighQuality, isRightToLeft, lease, colorFilter, offset, alpha, cachedImage, secondaryCachedImage);
    }

    /// <summary>
    /// Each <see cref="ContinuousPageEntry.Rect"/> is already the final on-screen placement -
    /// <see cref="ReaderLayoutModel.ComputeContinuousLayout"/> already applied fit/zoom/pan/scroll,
    /// so this is a straight scale-and-draw per visible page, no per-page fit math. No rotation
    /// support in continuous mode this pass (spec §9's named scope). See <see cref="OnRender"/>'s own
    /// doc comment for why the <paramref name="lease"/>/<paramref name="colorFilter"/> branch can't
    /// just wrap the existing <c>context.DrawBitmap</c> path.
    /// </summary>
    private static void RenderContinuous(ImmediateDrawingContext context, ReaderContinuousVisualData data, ISkiaSharpApiLease? lease, SKColorFilter? colorFilter)
    {
        var mode = data.HighQuality ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.LowQuality;
        foreach (var entry in data.Pages)
        {
            if (entry.Bitmap is null || entry.Rect.Width <= 0 || entry.Rect.Height <= 0)
            {
                continue;
            }

            // Pages entirely outside the viewport (the virtualization fringe, kept decoded for
            // smoothness but not on-screen right now) don't need a draw call at all.
            if (!entry.Rect.Intersects(new Rect(data.Bounds)))
            {
                continue;
            }

            if (lease is not null && colorFilter is not null)
            {
                var pixelSize = entry.Bitmap.PixelSize;
                using var skImage = SkiaBitmapConverter.ToSkImage(entry.Bitmap);
                using var paint = new SKPaint { ColorFilter = colorFilter, IsAntialias = true };
                var sourceRect = new SKRect(0, 0, pixelSize.Width, pixelSize.Height);
                var destRectSk = new SKRect((float)entry.Rect.X, (float)entry.Rect.Y, (float)(entry.Rect.X + entry.Rect.Width), (float)(entry.Rect.Y + entry.Rect.Height));
                lease.SkCanvas.DrawImage(skImage, sourceRect, destRectSk, paint);
                continue;
            }

            var targetSize = new PixelSize(Math.Max(1, (int)Math.Round(entry.Rect.Width)), Math.Max(1, (int)Math.Round(entry.Rect.Height)));
            using var scaled = entry.Bitmap.CreateScaledBitmap(targetSize, mode);
            context.DrawBitmap(scaled, new Rect(0, 0, targetSize.Width, targetSize.Height), entry.Rect);
        }
    }
}
