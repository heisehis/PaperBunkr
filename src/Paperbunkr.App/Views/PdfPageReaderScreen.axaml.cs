using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class PdfPageReaderScreen : UserControl
{
    public PdfPageReaderScreen()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Converts <see cref="CaptureOverlay.RegionCaptured"/>'s raw screen-space rect into a fraction
    /// (0-1) of the currently-rendered page's own bounds (docs/superpowers/specs/2026-09-01-books-
    /// reader-ergonomics-and-annotations-design.md §"PDF area capture"), using
    /// <see cref="PageCanvas.GetCurrentImageBounds"/> - the one piece of visual-tree access the view
    /// model itself can't do. <c>CaptureOverlayControl</c> and <c>PageCanvasControl</c> are stacked at
    /// the same size/position, so both rects already share one coordinate space.
    /// </summary>
    private void OnRegionCaptured(object? sender, Rect e)
    {
        if (DataContext is not PdfPageReaderScreenViewModel vm)
        {
            return;
        }

        var imageBounds = PageCanvasControl.GetCurrentImageBounds();
        if (imageBounds.Width <= 0 || imageBounds.Height <= 0)
        {
            return;
        }

        double x = Math.Clamp((e.X - imageBounds.X) / imageBounds.Width, 0, 1);
        double y = Math.Clamp((e.Y - imageBounds.Y) / imageBounds.Height, 0, 1);
        double width = Math.Clamp(e.Width / imageBounds.Width, 0, 1 - x);
        double height = Math.Clamp(e.Height / imageBounds.Height, 0, 1 - y);

        vm.CaptureRegion(new Rect(x, y, width, height));
    }

    private void OnCapturesScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is PdfPageReaderScreenViewModel vm)
        {
            vm.CloseCapturesCommand.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>Tapping the dimmed backdrop behind the Font &amp; Theme sheet closes it - same pattern <see cref="OnCapturesScrimPointerPressed"/> already used, extended for the new PDF theme sheet (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md, Step 7/8).</summary>
    private void OnFontThemeScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is PdfPageReaderScreenViewModel vm)
        {
            vm.CloseFontThemeCommand.Execute(null);
        }

        e.Handled = true;
    }
}
