using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// One book spine in the Library's bookshelf view (Cosmetics pitch #19, docs/superpowers/specs/2026-10-12-cosmetics-pitch-completion-design.md C).
/// A single custom-rendered control that reads its <see cref="StyledElement.DataContext"/> (no template parts, no per-tile bindings), the same rule
/// <see cref="TileCosmeticsOverlay"/> follows because every extra part is paid for on every realized card. It stands on the shelf board at the bottom of
/// its cell, takes its colour from the cover (<see cref="SpineColorCache"/>; a neutral spine until that is known), runs the title down the spine and shows
/// reading progress as a thin strip along its foot. Hover lifts it a few pixels; the parent button's pointer events drive that, so the spine itself
/// stays non-hit-testable and can never steal a click. Selection is the one property it listens to, and only while attached to the tree.
/// </summary>
public sealed class SpineTile : Control
{
    private const double Lift = 6;
    private const double Radius = 3;

    private static readonly IBrush Cylinder = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0x55, 0, 0, 0), 0.0),
            new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.22),
            new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.62),
            new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.82),
            new GradientStop(Color.FromArgb(0x50, 0, 0, 0), 1.0),
        },
    }.ToImmutable();

    private static readonly IBrush TopBand = new ImmutableSolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush ProgressTrack = new ImmutableSolidColorBrush(Color.FromArgb(0x70, 0, 0, 0));

    private static readonly Typeface TitleFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface NumberFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

    private INotifyPropertyChanged? _watched;
    private Control? _hoverHost;
    private bool _hover;
    private bool _focused;
    private bool _selected;

    public SpineTile()
    {
        IsHitTestVisible = false;
        DataContextChanged += (_, _) =>
        {
            Rewatch();
            InvalidateVisual();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _hoverHost = this.FindAncestorOfType<Button>();
        if (_hoverHost is not null)
        {
            _hoverHost.PointerEntered += OnHostEntered;
            _hoverHost.PointerExited += OnHostExited;
            _hoverHost.GotFocus += OnHostFocusChanged;
            _hoverHost.LostFocus += OnHostFocusChanged;
        }

        Rewatch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_hoverHost is not null)
        {
            _hoverHost.PointerEntered -= OnHostEntered;
            _hoverHost.PointerExited -= OnHostExited;
            _hoverHost.GotFocus -= OnHostFocusChanged;
            _hoverHost.LostFocus -= OnHostFocusChanged;
            _hoverHost = null;
        }

        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnItemPropertyChanged;
            _watched = null;
        }

        _hover = false;
        _focused = false;
    }

    private void Rewatch()
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnItemPropertyChanged;
            _watched = null;
        }

        _selected = (DataContext as ISelectableCard)?.IsSelected == true;
        if (VisualRoot is not null && DataContext is INotifyPropertyChanged notifier)
        {
            _watched = notifier;
            notifier.PropertyChanged += OnItemPropertyChanged;
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISelectableCard.IsSelected))
        {
            _selected = (DataContext as ISelectableCard)?.IsSelected == true;
            InvalidateVisual();
        }
    }

    private void OnHostEntered(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        _hover = true;
        InvalidateVisual();
    }

    private void OnHostFocusChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _focused = _hoverHost?.IsKeyboardFocusWithin == true;
        InvalidateVisual();
    }

    private void OnHostExited(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        _hover = false;
        _focused = false;
        InvalidateVisual();
    }

    private IBrush Resolve(string key, Color fallback) => this.TryFindResource(key, out object? found) && found is IBrush brush ? brush : new ImmutableSolidColorBrush(fallback);

    private Color? KnownColor()
    {
        if (DataContext is not ICoverKeyProvider { CoverKey: { Length: > 0 } key })
        {
            return null;
        }

        if (SpineColorCache.TryGet(key, out var color))
        {
            return color;
        }

        SpineColorCache.Request(key, InvalidateVisual);
        return null;
    }

    /// <summary>
    /// Asks for a full shelf-row height, so the tile still has a height when its host does not stretch it (a Button centres its content
    /// vertically by default, which measured this control at zero height and drew no spines at all).
    /// </summary>
    protected override Size MeasureOverride(Size availableSize) =>
        new(0, double.IsInfinity(availableSize.Height) ? SpineMetrics.CellHeight : Math.Min(availableSize.Height, SpineMetrics.CellHeight));

    public override void Render(DrawingContext context)
    {
        if (DataContext is not ISpineSource spine || Bounds.Width <= 2 || Bounds.Height <= 2)
        {
            return;
        }

        double width = Math.Min(spine.SpineWidth, Bounds.Width);
        double height = Math.Min(spine.SpineHeight, Bounds.Height - SpineMetrics.BoardHeight);
        double x = (Bounds.Width - width) / 2;
        double y = Bounds.Height - SpineMetrics.BoardHeight - height - ((_hover || _focused) ? Lift : 0);
        var rect = new Rect(x, y, width, height);

        if (spine.IsSpineGhost)
        {
            RenderGhost(context, spine, new Rect(x, Bounds.Height - SpineMetrics.BoardHeight - height, width, height));
            return;
        }

        var known = KnownColor();
        Color body;
        bool neutral = known is null;
        if (known is { } dominant)
        {
            body = SpineMetrics.BodyColor(dominant);
        }
        else if (Resolve("PbSurface3Brush", Color.FromRgb(0x2A, 0x2D, 0x36)) is ISolidColorBrush surface)
        {
            body = surface.Color;
        }
        else
        {
            body = Color.FromRgb(0x2A, 0x2D, 0x36);
        }

        bool darkText = SpineMetrics.UseDarkText(body);
        var ink = darkText ? new ImmutableSolidColorBrush(Color.FromRgb(0x14, 0x15, 0x19)) : new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF0));

        // Soft contact shadow on the board, wider while lifted.
        context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb((_hover || _focused) ? (byte)0x50 : (byte)0x38, 0, 0, 0)), null,
            new Rect(x - 1, Bounds.Height - SpineMetrics.BoardHeight - 3, width + 2, 4), 2, 2);

        var corners = new RoundedRect(rect, Radius, Radius, 0, 0);
        context.DrawRectangle(new ImmutableSolidColorBrush(body), null, corners);
        context.DrawRectangle(Cylinder, null, corners);
        context.DrawRectangle(TopBand, null, new RoundedRect(new Rect(x, y, width, 7), Radius, Radius, 0, 0));
        if (neutral)
        {
            // No cover colour yet (or none usable): a quiet accent stripe so the spine still reads as a book, not a blank.
            context.DrawRectangle(Resolve("PbAccentBrush", Color.FromRgb(0x5B, 0x8D, 0xEF)), null, new Rect(x + 3, y + 12, width - 6, 2));
        }

        // Title running down the spine, top to bottom.
        double numberReserve = spine.SpineNumber is null ? 8 : 24;
        double textLength = height - 18 - numberReserve;
        if (textLength > 24 && width >= 20)
        {
            var title = new FormattedText(spine.SpineTitle ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TitleFace,
                Math.Clamp(width * 0.36, 10, 14), ink)
            {
                MaxTextWidth = textLength,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            var origin = new Point(x + ((width + title.Height) / 2), y + 16);
            using (context.PushTransform(Matrix.CreateTranslation(-origin.X, -origin.Y) * Matrix.CreateRotation(Math.PI / 2) * Matrix.CreateTranslation(origin.X, origin.Y)))
            {
                context.DrawText(title, origin);
            }
        }

        if (spine.SpineNumber is { } number && width >= 22)
        {
            var cap = new FormattedText(number, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, NumberFace, 9.5, ink)
            {
                MaxTextWidth = width - 2,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            context.DrawText(cap, new Point(x + ((width - cap.Width) / 2), y + height - 22));
        }

        if (DataContext is ITileProgressSource progress)
        {
            double strip = 3;
            var foot = new Rect(x + 3, y + height - strip - 3, width - 6, strip);
            if (progress.IsFinished)
            {
                context.DrawRectangle(Resolve("PbAccentBrush", Color.FromRgb(0x5B, 0x8D, 0xEF)), null, foot, 1.5, 1.5);
            }
            else if (TileCosmeticsOverlay.ShowsProgressBar(progress.ReadFraction, progress.IsFinished))
            {
                context.DrawRectangle(ProgressTrack, null, foot, 1.5, 1.5);
                context.DrawRectangle(Resolve("PbAccentBrush", Color.FromRgb(0x5B, 0x8D, 0xEF)), null,
                    new Rect(foot.X, foot.Y, foot.Width * Math.Clamp(progress.ReadFraction, 0, 1), foot.Height), 1.5, 1.5);
            }
        }

        if (_selected)
        {
            context.DrawRectangle(null, new Pen(Resolve("PbAccentBrush", Color.FromRgb(0x5B, 0x8D, 0xEF)), 2), rect.Inflate(1), Radius, Radius);
        }
    }

    /// <summary>A missing book in a series: a dashed outline in the faint ink with its number at the foot. No fill, no lift, no progress.</summary>
    private void RenderGhost(DrawingContext context, ISpineSource spine, Rect rect)
    {
        var faint = Resolve("PbTextFaintBrush", Color.FromRgb(0x77, 0x72, 0x6A));
        var dashed = new Pen(faint, 1, new ImmutableDashStyle([3, 3], 0));
        context.DrawRectangle(null, dashed, rect.Deflate(0.5), Radius, Radius);

        if (spine.SpineNumber is { } number && rect.Width >= 18)
        {
            var cap = new FormattedText(number, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, NumberFace, 9.5, faint)
            {
                MaxTextWidth = rect.Width - 2,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            context.DrawText(cap, new Point(rect.X + ((rect.Width - cap.Width) / 2), rect.Bottom - 22));
        }
    }
}
