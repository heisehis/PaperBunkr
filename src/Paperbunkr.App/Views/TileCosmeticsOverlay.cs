using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// One custom-rendered overlay for a Library Poster/Panorama cover: the binding spine (docs/superpowers/specs/2026-09-21-
/// cosmetics-pitch-design.md #1) and the read-progress bar along the bottom edge (docs/superpowers/specs/2026-10-04-library-
/// redesign-design.md, Slice 2 - it replaced the hover progress ring, so progress reads at a glance and the bottom-right
/// corner is free for the rating and dog-ear). Deliberately a single <see cref="Control"/> with no template, no bindings and
/// no per-tile subscriptions beyond one static event: the scroll-smoothness pass (2026-09-19) priced every extra part in a
/// card's realization cost. It reads its own <see cref="StyledElement.DataContext"/> as an <see cref="ITileProgressSource"/>.
/// Never hit-testable, so it can't steal clicks from the tile.
/// </summary>
public sealed class TileCosmeticsOverlay : Control
{
    private const double SpineWidth = 16;

    /// <summary>Height of the progress bar. Thin enough to leave the art alone, thick enough to read on a 150px cover.</summary>
    public const double BarHeight = 4;

    // Flat overlays drawn over arbitrary cover art, so they are fixed neutral tones (black shade,
    // white highlight) rather than skin tokens; only the bar's value is themed.
    // One soft crease, no hard 1px line (a hard highlight line read as a stray scan line on-screen): a shade at
    // the edge, a slightly darker hinge groove ~8px in, a faint highlight just past it, then fading to nothing.
    // Offsets are fractions of SpineWidth. Built mirrored for right-to-left.
    private static LinearGradientBrush BuildSpine(bool mirrored) => new()
    {
        StartPoint = new RelativePoint(mirrored ? 1 : 0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(mirrored ? 0 : 1, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0x66, 0, 0, 0), 0.0),
            new GradientStop(Color.FromArgb(0x40, 0, 0, 0), 0.30),
            new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 0.50),
            new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.66),
            new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0),
        },
    };

    private static readonly IBrush SpineLeft = BuildSpine(false).ToImmutable();
    private static readonly IBrush SpineRight = BuildSpine(true).ToImmutable();
    private static readonly IBrush BarTrack = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)).ToImmutable();

    public TileCosmeticsOverlay()
    {
        IsHitTestVisible = false;
        DataContextChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>
    /// Whether a cover gets a progress bar: only while it is in progress. Nothing read draws nothing, and a finished one is
    /// marked by its badge instead, so a full bar never sits under every cover of a read-through library. Pure, so the rule
    /// is unit-tested.
    /// </summary>
    public static bool ShowsProgressBar(double readFraction, bool isFinished) =>
        !isFinished && readFraction > 0.005 && readFraction < 0.999;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CosmeticThumbnailSettings.OverlaySettingsChanged += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CosmeticThumbnailSettings.OverlaySettingsChanged -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        if (DataContext is not ITileProgressSource source || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        if (CosmeticThumbnailSettings.BindingSpine)
        {
            DrawSpine(context, source.IsRightToLeft);
        }

        if (CosmeticThumbnailSettings.ProgressRing && ShowsProgressBar(source.ReadFraction, source.IsFinished))
        {
            DrawBar(context, source);
        }
    }

    private void DrawSpine(DrawingContext context, bool rightToLeft)
    {
        double h = Bounds.Height;
        double w = Bounds.Width;
        context.DrawRectangle(rightToLeft ? SpineRight : SpineLeft, null, rightToLeft
            ? new Rect(w - SpineWidth, 0, SpineWidth, h)
            : new Rect(0, 0, SpineWidth, h));
    }

    /// <summary>The bar fills from the side reading starts on: left for left-to-right, right for a right-to-left book.</summary>
    private void DrawBar(DrawingContext context, ITileProgressSource source)
    {
        double w = Bounds.Width;
        double top = Bounds.Height - BarHeight;
        double filled = Math.Round(w * Math.Clamp(source.ReadFraction, 0.0, 1.0));

        context.DrawRectangle(BarTrack, null, new Rect(0, top, w, BarHeight));
        context.DrawRectangle(ResolveBrush("PbAccentBrush", Colors.Orange), null, source.IsRightToLeft
            ? new Rect(w - filled, top, filled, BarHeight)
            : new Rect(0, top, filled, BarHeight));
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
}
