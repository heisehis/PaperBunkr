using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A page-level atmosphere: an already-blurred image drawn across the top of a page and faded out towards the bottom with an opacity
/// mask, so the page's own background shows through more and more until nothing of the image is left. There is no fill behind the image
/// and no colour of its own, which is the point: whatever theme background is underneath is what it blends into, so it has no edge to
/// see in a light or a dark theme. Code-only (no .axaml). Draws once per invalidation and never animates, so there is nothing for Reduce
/// motion to stop; it does not take input.
/// </summary>
public sealed class AtmosphereBackdrop : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<AtmosphereBackdrop, IImage?>(nameof(Source));

    /// <summary>How much of the image shows where it is strongest (0 to 1). The rest is the page behind it.</summary>
    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<AtmosphereBackdrop, double>(nameof(Strength), 0.6);

    /// <summary>How far down (0 to 1 of the height) the image stays at full strength before it starts to fade.</summary>
    public static readonly StyledProperty<double> FadeStartProperty =
        AvaloniaProperty.Register<AtmosphereBackdrop, double>(nameof(FadeStart), 0.3);

    static AtmosphereBackdrop()
    {
        AffectsRender<AtmosphereBackdrop>(SourceProperty, StrengthProperty, FadeStartProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<AtmosphereBackdrop>(false);
        ClipToBoundsProperty.OverrideDefaultValue<AtmosphereBackdrop>(true);
    }

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    public double FadeStart
    {
        get => GetValue(FadeStartProperty);
        set => SetValue(FadeStartProperty, value);
    }

    /// <summary>
    /// The top-to-bottom mask: full strength down to <paramref name="fadeStart"/>, then an eased fall to nothing at the bottom edge. Only
    /// its alpha matters. The last stop is always fully transparent, which is what makes the bottom edge disappear.
    /// </summary>
    public static LinearGradientBrush BuildMask(double strength, double fadeStart)
    {
        strength = Math.Clamp(strength, 0, 1);
        fadeStart = Math.Clamp(fadeStart, 0, 0.95);

        Color At(double fraction) => Color.FromArgb((byte)Math.Round(255 * strength * fraction), 0, 0, 0);
        double Along(double t) => fadeStart + ((1 - fadeStart) * t);

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(At(1), 0),
                new GradientStop(At(1), fadeStart),

                // An ease-out tail rather than a straight ramp: a straight ramp still reads as a band where it starts and ends.
                new GradientStop(At(0.72), Along(0.25)),
                new GradientStop(At(0.42), Along(0.5)),
                new GradientStop(At(0.16), Along(0.75)),
                new GradientStop(At(0.04), Along(0.9)),
                new GradientStop(At(0), 1),
            },
        };
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } image || Bounds.Width <= 0 || Bounds.Height <= 0 || Strength <= 0)
        {
            return;
        }

        var size = image.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        // UniformToFill: crop the source to this control's aspect ratio, centred.
        var dest = new Rect(Bounds.Size);
        double destAspect = dest.Width / dest.Height;
        double srcAspect = size.Width / size.Height;
        Rect src = srcAspect > destAspect
            ? new Rect((size.Width - (size.Height * destAspect)) / 2, 0, size.Height * destAspect, size.Height)
            : new Rect(0, (size.Height - (size.Width / destAspect)) / 2, size.Width, size.Width / destAspect);

        using (context.PushOpacityMask(BuildMask(Strength, FadeStart), dest))
        {
            context.DrawImage(image, src, dest);
        }
    }
}
