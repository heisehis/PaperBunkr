using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Controls.Books;

/// <summary>
/// A soft radial wash in a book's own spine colour behind the Books spread and the Detail jacket (docs/superpowers/specs/2026-10-27-books-
/// section-redesign-design.md §2, "B: the Shelf"). The colour comes from <see cref="SpineColorCache"/> for <see cref="CoverKey"/>
/// (<c>book:{id}</c>); until it is known, or when there is none, the theme's accent stands in. It is a tint over the theme background, never a
/// dark blurred cover, so theme text keeps its contrast: lighter on a light theme. <c>PbCoverWashAccentMix</c> (0-1) pulls the colour toward
/// the accent for a theme where cover colours clash. Not hit-testable; put it behind the content (it is a Border, so it can hold the content).
/// </summary>
public sealed class CoverWash : Border
{
    public const double DarkAlpha = 0.38;
    public const double LightAlpha = 0.22;

    public static readonly StyledProperty<string?> CoverKeyProperty =
        AvaloniaProperty.Register<CoverWash, string?>(nameof(CoverKey));

    /// <summary>Where the wash is brightest, relative to the control (0-1 each way).</summary>
    public static readonly StyledProperty<RelativePoint> FocusPointProperty =
        AvaloniaProperty.Register<CoverWash, RelativePoint>(nameof(FocusPoint), new RelativePoint(0.18, 0.25, RelativeUnit.Relative));

    public CoverWash()
    {
        ActualThemeVariantChanged += (_, _) => Apply();
    }

    public string? CoverKey
    {
        get => GetValue(CoverKeyProperty);
        set => SetValue(CoverKeyProperty, value);
    }

    public RelativePoint FocusPoint
    {
        get => GetValue(FocusPointProperty);
        set => SetValue(FocusPointProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CoverKeyProperty || change.Property == FocusPointProperty)
        {
            Apply();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Apply();
    }

    /// <summary>The wash colour for a cover colour, the theme's accent and the accent mix, at full opacity.</summary>
    public static Color WashColor(Color? cover, Color accent, double accentMix)
    {
        var baseColor = cover ?? accent;
        double t = Math.Clamp(accentMix, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(baseColor.R + ((accent.R - baseColor.R) * t)),
            (byte)Math.Round(baseColor.G + ((accent.G - baseColor.G) * t)),
            (byte)Math.Round(baseColor.B + ((accent.B - baseColor.B) * t)));
    }

    private void Apply()
    {
        if (VisualRoot is null)
        {
            return;
        }

        Color? cover = null;
        if (CoverKey is { Length: > 0 } key)
        {
            if (SpineColorCache.TryGet(key, out var known))
            {
                cover = known;
            }
            else
            {
                SpineColorCache.Request(key, () => Avalonia.Threading.Dispatcher.UIThread.Post(Apply));
            }
        }

        var accent = this.TryFindResource("PbAccentColor", ActualThemeVariant, out object? a) && a is Color ac ? ac : Colors.Gray;
        double mix = this.TryFindResource("PbCoverWashAccentMix", ActualThemeVariant, out object? m) && m is double d ? d : 0;
        var color = WashColor(cover, accent, mix);
        double alpha = ActualThemeVariant == ThemeVariant.Light ? LightAlpha : DarkAlpha;

        Background = new RadialGradientBrush
        {
            Center = FocusPoint,
            GradientOrigin = FocusPoint,
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
    }
}
