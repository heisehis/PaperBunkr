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
    int RotationDegrees);

/// <summary>One page's already-computed on-screen placement (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §2/§4) - <see cref="Rect"/> comes straight from <see cref="ReaderLayoutModel.ComputeContinuousLayout"/>, already in viewport space, so the handler just scales-and-draws rather than repeating fit/pan math per page.</summary>
internal readonly record struct ContinuousPageEntry(Rect Rect, Bitmap? Bitmap);

/// <summary>Continuous mode's per-frame draw data - unlike <see cref="ReaderPageVisualData"/>'s single bitmap, a whole page list, each with its own placement. No fit-mode/rotation fields - continuous mode has neither (spec §5/§9's named scope, rotation isn't part of this pass for continuous mode).</summary>
internal sealed record ReaderContinuousVisualData(Size Bounds, IReadOnlyList<ContinuousPageEntry> Pages, bool HighQuality);

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
                Invalidate();
                break;
            case ReaderContinuousVisualData continuous:
                _continuousData = continuous;
                _pagedData = null;
                Invalidate();
                break;
            case AdjustmentVisualData adjustment:
                _adjustmentData = adjustment;
                Invalidate();
                break;
        }
    }

    /// <summary>Falls back to the visual's own <see cref="CompositionCustomVisualHandler.EffectiveSize"/> before the first message arrives, so the compositor has a sane bounds to reason about even on the very first compose pass.</summary>
    public override Rect GetRenderBounds() => _pagedData?.Bounds ?? (_continuousData is { } c ? new Rect(c.Bounds) : new Rect(0, 0, EffectiveSize.X, EffectiveSize.Y));

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
        using ISkiaSharpApiLease? lease = colorFilter is not null && context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is { } leaseFeature
            ? leaseFeature.Lease()
            : null;

        if (_continuousData is { } continuous)
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
    /// Byte-for-byte the same fit/pan/rotation math as <see cref="ReaderPageDrawOperation"/>'s
    /// predecessor <c>Render</c> method - only the draw call at the end branches on whether a
    /// <paramref name="lease"/>/<paramref name="colorFilter"/> is active (see <see cref="OnRender"/>'s
    /// own doc comment for why that can't just wrap the existing <c>context.DrawBitmap</c> path).
    /// </summary>
    private static void RenderPaged(ImmediateDrawingContext context, ReaderPageVisualData data, ISkiaSharpApiLease? lease, SKColorFilter? colorFilter)
    {
        if (data.Bitmap is null || data.Bounds.Width <= 0 || data.Bounds.Height <= 0)
        {
            return;
        }

        var bitmap = data.Bitmap;
        var pixelSize = bitmap.PixelSize;

        bool swapped = data.RotationDegrees is 90 or 270;
        var effectivePixelSize = swapped ? new PixelSize(pixelSize.Height, pixelSize.Width) : pixelSize;

        double scale = ZoomPanMath.ComputeBaseScale(data.Bounds.Size, effectivePixelSize, data.FitMode, data.FitOnlyIfOversized) * data.Zoom;

        var (panX, panY) = ZoomPanMath.ClampPan(data.Bounds.Size, effectivePixelSize, data.Zoom, data.PanOffsetX, data.PanOffsetY, data.FitMode, data.FitOnlyIfOversized);

        double centerX = data.Bounds.X + (data.Bounds.Width / 2) + panX;
        double centerY = data.Bounds.Y + (data.Bounds.Height / 2) + panY;

        double nativeWidth = pixelSize.Width * scale;
        double nativeHeight = pixelSize.Height * scale;
        var destRect = new Rect(centerX - (nativeWidth / 2), centerY - (nativeHeight / 2), nativeWidth, nativeHeight);

        if (lease is not null && colorFilter is not null)
        {
            using var skImage = SkiaBitmapConverter.ToSkImage(bitmap);
            using var paint = new SKPaint { ColorFilter = colorFilter, IsAntialias = true };
            var sourceRect = new SKRect(0, 0, pixelSize.Width, pixelSize.Height);
            var destRectSk = new SKRect((float)destRect.X, (float)destRect.Y, (float)(destRect.X + destRect.Width), (float)(destRect.Y + destRect.Height));

            if (data.RotationDegrees == 0)
            {
                lease.SkCanvas.DrawImage(skImage, sourceRect, destRectSk, paint);
                return;
            }

            int saveCount = lease.SkCanvas.Save();
            lease.SkCanvas.RotateDegrees(data.RotationDegrees, (float)centerX, (float)centerY);
            lease.SkCanvas.DrawImage(skImage, sourceRect, destRectSk, paint);
            lease.SkCanvas.RestoreToCount(saveCount);
            return;
        }

        var targetSize = new PixelSize(Math.Max(1, (int)Math.Round(nativeWidth)), Math.Max(1, (int)Math.Round(nativeHeight)));
        var mode = data.HighQuality ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.LowQuality;
        using var scaled = bitmap.CreateScaledBitmap(targetSize, mode);

        if (data.RotationDegrees == 0)
        {
            context.DrawBitmap(scaled, new Rect(0, 0, targetSize.Width, targetSize.Height), destRect);
            return;
        }

        double radians = data.RotationDegrees * Math.PI / 180;
        var rotateAroundCenter = Matrix.CreateTranslation(-centerX, -centerY) * Matrix.CreateRotation(radians) * Matrix.CreateTranslation(centerX, centerY);
        using (context.PushPostTransform(rotateAroundCenter))
        {
            context.DrawBitmap(scaled, new Rect(0, 0, targetSize.Width, targetSize.Height), destRect);
        }
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
