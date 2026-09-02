using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Paperbunkr.App.Views;

/// <summary>
/// Transparent click-drag selection-rectangle overlay for the PDF reader's "Capture Region" tool
/// (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-design.md §"PDF area
/// capture"). Deliberately simple compared to <see cref="ParagraphView"/> - this is plain rectangle
/// drawing over a <see cref="Media.Imaging.Bitmap"/>, not text layout, so it needs none of that
/// control's <c>TextLayout</c>/hit-testing machinery. Stacked directly on top of <c>PageCanvas</c> at
/// the same size/position in <c>PdfPageReaderScreen.axaml</c>, so <see cref="RegionCaptured"/>'s rect
/// is already in the same coordinate space <c>PageCanvas.GetCurrentImageBounds()</c> returns.
/// </summary>
public sealed class CaptureOverlay : Control
{
    public static readonly StyledProperty<bool> IsCaptureModeProperty =
        AvaloniaProperty.Register<CaptureOverlay, bool>(nameof(IsCaptureMode));

    static CaptureOverlay()
    {
        AffectsRender<CaptureOverlay>(IsCaptureModeProperty);
    }

    public bool IsCaptureMode
    {
        get => GetValue(IsCaptureModeProperty);
        set => SetValue(IsCaptureModeProperty, value);
    }

    /// <summary>Fires once per completed drag, with the selected rect in this control's own coordinate space.</summary>
    public event EventHandler<Rect>? RegionCaptured;

    private static readonly IBrush FillBrush = new SolidColorBrush(Color.Parse("#3364B5F6"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Colors.DodgerBlue), 1.5);

    private Point? _dragStart;
    private Point? _dragCurrent;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!IsCaptureMode || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragCurrent = _dragStart;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragStart is null)
        {
            return;
        }

        _dragCurrent = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragStart is not { } start || _dragCurrent is not { } current)
        {
            return;
        }

        e.Pointer.Capture(null);
        _dragStart = null;
        _dragCurrent = null;
        InvalidateVisual();

        var rect = NormalizedRect(start, current);
        if (rect.Width >= 4 && rect.Height >= 4)
        {
            RegionCaptured?.Invoke(this, rect);
        }
    }

    private static Rect NormalizedRect(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    public override void Render(DrawingContext context)
    {
        if (_dragStart is not { } start || _dragCurrent is not { } current)
        {
            return;
        }

        var rect = NormalizedRect(start, current);
        context.FillRectangle(FillBrush, rect);
        context.DrawRectangle(BorderPen, rect);
    }
}
