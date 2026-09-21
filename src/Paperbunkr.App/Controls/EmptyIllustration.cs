using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Controls;

/// <summary>Which line drawing an <see cref="EmptyIllustration"/> shows.</summary>
public enum EmptyIllustrationKind
{
    Stack,
    List,
    Timeline,
    Calendar,
    Bookmark,
}

/// <summary>
/// Small vector line illustration for an empty state (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #13). Drawn
/// from path data in the skin's faint-foreground colour (<c>PbTextFaintBrush</c>), so it follows every theme and needs no image
/// assets. It sits above the empty state's existing message and call-to-action buttons; it never carries meaning on its own
/// (decorative, no automation peer content). One shared geometry per kind, parsed once. Round caps/joins, 1.6 px stroke.
/// </summary>
public sealed class EmptyIllustration : Control
{
    public const double DrawingWidth = 96;
    public const double DrawingHeight = 72;

    public static readonly StyledProperty<EmptyIllustrationKind> KindProperty =
        AvaloniaProperty.Register<EmptyIllustration, EmptyIllustrationKind>(nameof(Kind));

    // All drawn in a 96 x 72 box.
    private static readonly Dictionary<EmptyIllustrationKind, Geometry> Shapes = new()
    {
        [EmptyIllustrationKind.Stack] = Geometry.Parse("M14,58 H82 V66 H14 Z M20,46 H76 V54 H20 Z M26,34 H70 V42 H26 Z M32,22 H64 V30 H32 Z"),
        [EmptyIllustrationKind.List] = Geometry.Parse("M14,18 H20 M28,18 H82 M14,36 H20 M28,36 H82 M14,54 H20 M28,54 H66"),
        [EmptyIllustrationKind.Timeline] = Geometry.Parse(
            "M28,6 V66 M28,20 m-5,0 a5,5 0 1,0 10,0 a5,5 0 1,0 -10,0 M28,36 m-5,0 a5,5 0 1,0 10,0 a5,5 0 1,0 -10,0 " +
            "M28,52 m-5,0 a5,5 0 1,0 10,0 a5,5 0 1,0 -10,0 M40,20 H82 M40,36 H72 M40,52 H84"),
        [EmptyIllustrationKind.Calendar] = Geometry.Parse("M14,16 H82 V64 H14 Z M14,30 H82 M28,10 V22 M68,10 V22 M26,40 H34 M44,40 H52 M62,40 H70 M26,52 H34 M44,52 H52"),
        [EmptyIllustrationKind.Bookmark] = Geometry.Parse("M32,8 H64 V64 L48,52 L32,64 Z"),
    };

    static EmptyIllustration()
    {
        AffectsRender<EmptyIllustration>(KindProperty);
        AffectsMeasure<EmptyIllustration>(KindProperty);
    }

    public EmptyIllustration()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public EmptyIllustrationKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(DrawingWidth, DrawingHeight);

    public override void Render(DrawingContext context)
    {
        var brush = this.TryFindResource("PbTextFaintBrush", out object? value) && value is IBrush b
            ? b
            : new SolidColorBrush(Color.FromRgb(0x8B, 0x8F, 0x9A));
        var pen = new Pen(brush, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        // Centre the 96x72 drawing in whatever space the layout gave us.
        double dx = Math.Max(0, (Bounds.Width - DrawingWidth) / 2);
        double dy = Math.Max(0, (Bounds.Height - DrawingHeight) / 2);
        using (context.PushTransform(Matrix.CreateTranslation(dx, dy)))
        {
            context.DrawGeometry(null, pen, Shapes[Kind]);
        }
    }
}
