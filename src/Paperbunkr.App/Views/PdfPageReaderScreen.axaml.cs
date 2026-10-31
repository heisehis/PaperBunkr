using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class PdfPageReaderScreen : UserControl
{
    public PdfPageReaderScreen()
    {
        InitializeComponent();
        // Books redesign §5.5: the page edges follow wherever (and however large) the page is drawn.
        PageCanvasControl.LayoutUpdated += (_, _) => SyncPageEdges();
        PageCanvasControl.PropertyChanged += (_, e) =>
        {
            if (e.Property == PageCanvas.PageProperty || e.Property == PageCanvas.ZoomLevelProperty
                || e.Property == PageCanvas.PanOffsetXProperty || e.Property == PageCanvas.PanOffsetYProperty)
            {
                Dispatcher.UIThread.Post(SyncPageEdges, DispatcherPriority.Render);
            }
        };
    }

    private void SyncPageEdges()
    {
        var bounds = PageCanvasControl.GetCurrentImageBounds();
        var origin = PageCanvasControl.TranslatePoint(default, PageEdgesControl) ?? default;
        var rect = new Rect(bounds.X + origin.X, bounds.Y + origin.Y, bounds.Width, bounds.Height);
        if (PageEdgesControl.PageBounds != rect)
        {
            PageEdgesControl.PageBounds = rect;
        }
    }

    private void OnClusterPrevious(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is PdfPageReaderScreenViewModel vm && vm.GoLeftCommand.CanExecute(null))
        {
            vm.GoLeftCommand.Execute(null);
        }
    }

    private void OnClusterNext(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is PdfPageReaderScreenViewModel vm && vm.GoRightCommand.CanExecute(null))
        {
            vm.GoRightCommand.Execute(null);
        }
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
            // Deferred: the scrim itself lives inside the drawer/sheet Popup's own content, so
            // closing synchronously here would detach that content mid pointer-event-route -
            // Avalonia's detach walk crashes with an ArgumentOutOfRangeException (see
            // Paperbunkr.App.Controls.SuggestBox.Commit for the fully diagnosed case, and
            // BookReaderScreen.axaml.cs.OnScrimPointerPressed for the same fix already applied there).
            Dispatcher.UIThread.Post(() => vm.CloseCapturesCommand.Execute(null));
        }

        e.Handled = true;
    }

    /// <summary>Tapping the dimmed backdrop behind the Font &amp; Theme sheet closes it - same pattern <see cref="OnCapturesScrimPointerPressed"/> already used, extended for the new PDF theme sheet (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md, Step 7/8).</summary>
    private void OnFontThemeScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is PdfPageReaderScreenViewModel vm)
        {
            // Deferred: same reason as OnCapturesScrimPointerPressed above.
            Dispatcher.UIThread.Post(() => vm.CloseFontThemeCommand.Execute(null));
        }

        e.Handled = true;
    }
}
