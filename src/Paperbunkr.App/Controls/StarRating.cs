using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A five-star rating in half-star steps (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §6): the book editors'
/// "My rating" (CE's <c>RatingControl</c> scale: 0-5 float, null = unrated). Drawn, not templated, like <see cref="ProgressRing"/>.
/// Click the left half of a star for a half, the right half for a whole; clicking the current value clears it. Left / Right move by half a
/// star and Delete clears (the control's own item keys, not app hotkeys). Brushes come from the shared style in Controls.axaml.
/// </summary>
public sealed class StarRating : Control
{
    private const string StarPath = "M12 2 L15.09 8.26 L22 9.27 L17 14.14 L18.18 21.02 L12 17.77 L5.82 21.02 L7 14.14 L2 9.27 L8.91 8.26 Z";

    private static readonly Geometry StarGeometry = Geometry.Parse(StarPath);

    public static readonly StyledProperty<double?> ValueProperty =
        AvaloniaProperty.Register<StarRating, double?>(nameof(Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, coerce: CoerceValue);

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<StarRating, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<double> StarSizeProperty =
        AvaloniaProperty.Register<StarRating, double>(nameof(StarSize), 16d);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<StarRating, double>(nameof(Spacing), 2d);

    public static readonly StyledProperty<IBrush?> FilledBrushProperty =
        AvaloniaProperty.Register<StarRating, IBrush?>(nameof(FilledBrush));

    public static readonly StyledProperty<IBrush?> EmptyBrushProperty =
        AvaloniaProperty.Register<StarRating, IBrush?>(nameof(EmptyBrush));

    private double? _hover;

    static StarRating()
    {
        AffectsRender<StarRating>(ValueProperty, FilledBrushProperty, EmptyBrushProperty, StarSizeProperty, SpacingProperty);
        AffectsMeasure<StarRating>(StarSizeProperty, SpacingProperty);
        FocusableProperty.OverrideDefaultValue<StarRating>(true);
        CursorProperty.OverrideDefaultValue<StarRating>(new Cursor(StandardCursorType.Hand));
    }

    public const int Stars = 5;

    /// <summary>0..5 in 0.5 steps; null = unrated. Out-of-range or off-grid values are snapped.</summary>
    public double? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public double StarSize
    {
        get => GetValue(StarSizeProperty);
        set => SetValue(StarSizeProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public IBrush? FilledBrush
    {
        get => GetValue(FilledBrushProperty);
        set => SetValue(FilledBrushProperty, value);
    }

    public IBrush? EmptyBrush
    {
        get => GetValue(EmptyBrushProperty);
        set => SetValue(EmptyBrushProperty, value);
    }

    private static double? CoerceValue(AvaloniaObject _, double? value)
    {
        if (value is not double v || double.IsNaN(v) || v <= 0d)
        {
            return null;
        }

        return Math.Round(Math.Min(v, Stars) * 2d, MidpointRounding.AwayFromZero) / 2d;
    }

    /// <summary>The rating a pointer at <paramref name="x"/> (control coordinates) would set: whole or half star under it.</summary>
    public static double ValueAt(double x, double starSize, double spacing)
    {
        double pitch = starSize + spacing;
        if (pitch <= 0d)
        {
            return 0d;
        }

        int index = (int)Math.Floor(x / pitch);
        index = Math.Clamp(index, 0, Stars - 1);
        double within = x - (index * pitch);
        return index + (within < starSize / 2d ? 0.5d : 1d);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Stars * StarSize + (Stars - 1) * Spacing, StarSize);

    public override void Render(DrawingContext context)
    {
        double size = StarSize;
        double pitch = size + Spacing;
        double shown = _hover ?? Value ?? 0d;
        var transform = Matrix.CreateScale(size / 24d, size / 24d);

        for (int i = 0; i < Stars; i++)
        {
            double fraction = Math.Clamp(shown - i, 0d, 1d);
            double x = i * pitch;

            using (context.PushTransform(Matrix.CreateTranslation(x, 0)))
            {
                using (context.PushTransform(transform))
                {
                    context.DrawGeometry(EmptyBrush, null, StarGeometry);
                }

                if (fraction > 0d)
                {
                    using (context.PushClip(new Rect(0, 0, size * fraction, size)))
                    using (context.PushTransform(transform))
                    {
                        context.DrawGeometry(FilledBrush, null, StarGeometry);
                    }
                }
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (IsReadOnly)
        {
            return;
        }

        _hover = ValueAt(e.GetPosition(this).X, StarSize, Spacing);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsReadOnly || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        double picked = ValueAt(e.GetPosition(this).X, StarSize, Spacing);
        Value = Value is double current && Math.Abs(current - picked) < 0.001 ? null : picked;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsReadOnly || e.Handled)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Right:
            case Key.Up:
                Value = Math.Min(Stars, (Value ?? 0d) + 0.5d);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Down:
                Value = (Value ?? 0d) - 0.5d;
                e.Handled = true;
                break;
            case Key.Delete:
            case Key.Back:
                Value = null;
                e.Handled = true;
                break;
        }
    }
}
