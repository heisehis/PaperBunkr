using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// One custom-rendered overlay for a Library Poster/Panorama cover (docs/superpowers/specs/2026-09-21-
/// cosmetics-pitch-design.md #1 binding spine, #2 read-progress ring). Deliberately a single
/// <see cref="Control"/> with no template, no bindings and no per-tile subscriptions beyond one static
/// event: the scroll-smoothness pass (2026-09-19) priced every extra part in a card's realization cost.
/// It reads its own <see cref="StyledElement.DataContext"/> as an <see cref="ITileProgressSource"/>, and
/// the ring's hover state is pushed in by <c>LibraryScreen</c>'s existing cover pointer handlers via
/// <see cref="HoverRing"/>. Never hit-testable, so it can't steal clicks from the tile.
/// </summary>
public sealed class TileCosmeticsOverlay : Control
{
    private const double SpineWidth = 16;
    private const double RingSize = 34;
    private const double RingStroke = 4;
    private const double RingBottomMargin = 7;

    // Flat overlays drawn over arbitrary cover art, so they are fixed neutral tones (black shade,
    // white highlight) rather than skin tokens; only the ring's value stroke is themed.
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
    private static readonly IBrush RingBackdrop = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)).ToImmutable();
    private static readonly IPen RingTrack = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), RingStroke).ToImmutable();

    private const double RingEdgeMargin = 7;

    private bool _hoverRing;
    private INotifyPropertyChanged? _watched;

    public TileCosmeticsOverlay()
    {
        IsHitTestVisible = false;
        DataContextChanged += (_, _) =>
        {
            UpdateSelectionWatch();
            InvalidateVisual();
        };
    }

    /// <summary>
    /// Where the ring's centre sits horizontally. Bottom-RIGHT when the tile shows no selection checkbox (the default now that multi-select is
    /// Ctrl/Shift+click), bottom-CENTRE when the checkbox owns that corner - either because the "Selection checkbox on tiles" setting is on, or because
    /// this tile is selected (a selected tile always shows its checked box). Pure so the placement rule is unit-tested.
    /// </summary>
    public static double RingCenterX(double width, bool checkboxOwnsBottomRight)
        => checkboxOwnsBottomRight ? width / 2 : width - RingEdgeMargin - (RingSize / 2);

    /// <summary>Set from the cover's PointerEntered/Exited handlers - true while the pointer is over the tile.</summary>
    public bool HoverRing
    {
        get => _hoverRing;
        set
        {
            if (_hoverRing == value)
            {
                return;
            }

            _hoverRing = value;
            UpdateSelectionWatch();
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CosmeticThumbnailSettings.OverlaySettingsChanged += OnSettingsChanged;
        UpdateSelectionWatch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CosmeticThumbnailSettings.OverlaySettingsChanged -= OnSettingsChanged;
        _hoverRing = false;
        UpdateSelectionWatch();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSettingsChanged()
    {
        UpdateSelectionWatch();
        InvalidateVisual();
    }

    /// <summary>Selecting a tile puts its checkbox in the bottom-right, so a ring drawn there must move. To repaint on that change we listen to the row's
    /// PropertyChanged - but only while the ring is actually showing (hovered, or a finished tile's at-rest ring), so the vast majority of realized
    /// tiles carry no subscription.</summary>
    private void UpdateSelectionWatch()
    {
        bool needed = VisualRoot is not null
            && CosmeticThumbnailSettings.ProgressRing
            && DataContext is ITileProgressSource { } source
            && (_hoverRing || source.IsFinished)
            && DataContext is INotifyPropertyChanged;
        var wanted = needed ? DataContext as INotifyPropertyChanged : null;
        if (ReferenceEquals(wanted, _watched))
        {
            return;
        }

        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnRowPropertyChanged;
        }

        _watched = wanted;
        if (_watched is not null)
        {
            _watched.PropertyChanged += OnRowPropertyChanged;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ISelectableCard.IsSelected))
        {
            InvalidateVisual();
        }
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

        if (CosmeticThumbnailSettings.ProgressRing && (_hoverRing || source.IsFinished))
        {
            DrawRing(context, source);
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

    private void DrawRing(DrawingContext context, ITileProgressSource source)
    {
        double radius = (RingSize - RingStroke) / 2;
        bool checkboxOwnsCorner = CosmeticThumbnailSettings.ShowSelectionCheckbox || DataContext is ISelectableCard { IsSelected: true };
        var center = new Point(RingCenterX(Bounds.Width, checkboxOwnsCorner), Bounds.Height - RingBottomMargin - (RingSize / 2));
        bool finished = source.IsFinished;
        double fraction = finished ? 1.0 : Math.Clamp(source.ReadFraction, 0.0, 1.0);

        context.DrawEllipse(RingBackdrop, null, center, RingSize / 2, RingSize / 2);
        context.DrawEllipse(null, RingTrack, center, radius, radius);

        var valueBrush = ResolveBrush(finished ? "PbSuccessBrush" : "PbAccentTextBrush", finished ? Colors.LightGreen : Colors.LightSkyBlue);
        var valuePen = new Pen(valueBrush, RingStroke) { LineCap = PenLineCap.Round };

        if (fraction >= 0.999)
        {
            context.DrawEllipse(null, valuePen, center, radius, radius);
        }
        else if (fraction > 0.005)
        {
            context.DrawGeometry(null, valuePen, ArcGeometry.CreateArc(center, radius, ArcGeometry.TopAngle, fraction * Math.PI * 2));
        }

        if (finished)
        {
            // A drawn check, not a glyph - no font-fallback dependency on a cover's overlay.
            var check = new StreamGeometry();
            using (var ctx = check.Open())
            {
                ctx.BeginFigure(new Point(center.X - 5, center.Y + 0.5), false);
                ctx.LineTo(new Point(center.X - 1.5, center.Y + 4));
                ctx.LineTo(new Point(center.X + 5.5, center.Y - 4));
                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(valueBrush, 2.2) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, check);
            return;
        }

        var text = new FormattedText(
            ((int)Math.Round(fraction * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default,
            11, Brushes.White);
        context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
}
