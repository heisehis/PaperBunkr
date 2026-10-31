using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Views.Reader;

/// <summary>
/// The PDF reader's page edges (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §5.5): fine stripes either side of the
/// rendered page, thin on the side you have read little of and thick on the other, the same 2-24 px as the EPUB page's own edges
/// (<see cref="PageEdgeMath"/>). Draws nothing until the host sets <see cref="PageBounds"/> (the page image's rectangle).
/// </summary>
public sealed class PageEdges : Control
{
    public static readonly StyledProperty<Rect> PageBoundsProperty = AvaloniaProperty.Register<PageEdges, Rect>(nameof(PageBounds));
    public static readonly StyledProperty<double> FractionProperty = AvaloniaProperty.Register<PageEdges, double>(nameof(Fraction));
    public static readonly StyledProperty<IBrush?> PageColorProperty = AvaloniaProperty.Register<PageEdges, IBrush?>(nameof(PageColor));
    public static readonly StyledProperty<IBrush?> InkColorProperty = AvaloniaProperty.Register<PageEdges, IBrush?>(nameof(InkColor));

    static PageEdges() => AffectsRender<PageEdges>(PageBoundsProperty, FractionProperty, PageColorProperty, InkColorProperty);

    public Rect PageBounds { get => GetValue(PageBoundsProperty); set => SetValue(PageBoundsProperty, value); }

    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    public IBrush? PageColor { get => GetValue(PageColorProperty); set => SetValue(PageColorProperty, value); }

    public IBrush? InkColor { get => GetValue(InkColorProperty); set => SetValue(InkColorProperty, value); }

    public override void Render(DrawingContext context)
    {
        var page = PageBounds;
        if (page.Width < 8 || page.Height < 8 || PageColor is null || InkColor is not ISolidColorBrush ink)
        {
            return;
        }

        var line = new Pen(new SolidColorBrush(Color.FromArgb(0x33, ink.Color.R, ink.Color.G, ink.Color.B)), 1);
        double left = PageEdgeMath.Width(Fraction);
        double right = PageEdgeMath.Width(1 - Fraction);
        DrawStack(context, new Rect(page.X - left, page.Y + 3, left, page.Height - 6), line);
        DrawStack(context, new Rect(page.Right, page.Y + 3, right, page.Height - 6), line);
    }

    private void DrawStack(DrawingContext context, Rect rect, Pen line)
    {
        if (rect.Width <= 0)
        {
            return;
        }

        context.DrawRectangle(PageColor, null, rect);
        for (double x = rect.X + 1.5; x < rect.Right; x += 3)
        {
            context.DrawLine(line, new Point(x, rect.Y), new Point(x, rect.Bottom));
        }
    }
}
