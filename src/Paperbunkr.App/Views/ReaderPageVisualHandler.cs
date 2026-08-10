using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Paperbunkr.Data.Entities;

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
        }
    }

    /// <summary>Falls back to the visual's own <see cref="CompositionCustomVisualHandler.EffectiveSize"/> before the first message arrives, so the compositor has a sane bounds to reason about even on the very first compose pass.</summary>
    public override Rect GetRenderBounds() => _pagedData?.Bounds ?? (_continuousData is { } c ? new Rect(c.Bounds) : new Rect(0, 0, EffectiveSize.X, EffectiveSize.Y));

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (_continuousData is { } continuous)
        {
            RenderContinuous(context, continuous);
            return;
        }

        if (_pagedData is { } paged)
        {
            RenderPaged(context, paged);
        }
    }

    /// <summary>
    /// Byte-for-byte the same fit/pan/rotation math and <see cref="ImmediateDrawingContext"/> draw
    /// calls as <see cref="ReaderPageDrawOperation"/>'s predecessor <c>Render</c> method - only the
    /// entry point changed (this overrides <see cref="CompositionCustomVisualHandler.OnRender"/>
    /// instead of implementing <c>ICustomDrawOperation.Render</c>), not the drawing logic itself.
    /// </summary>
    private static void RenderPaged(ImmediateDrawingContext context, ReaderPageVisualData data)
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
    /// support in continuous mode this pass (spec §9's named scope).
    /// </summary>
    private static void RenderContinuous(ImmediateDrawingContext context, ReaderContinuousVisualData data)
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

            var targetSize = new PixelSize(Math.Max(1, (int)Math.Round(entry.Rect.Width)), Math.Max(1, (int)Math.Round(entry.Rect.Height)));
            using var scaled = entry.Bitmap.CreateScaledBitmap(targetSize, mode);
            context.DrawBitmap(scaled, new Rect(0, 0, targetSize.Width, targetSize.Height), entry.Rect);
        }
    }
}
