using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class BookReaderScreen : UserControl
{
    public BookReaderScreen()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;

        // SizeChanged alone isn't reliable for the very first time this screen becomes visible:
        // if this control was already measured with its final size while IsVisible was still
        // false (its ContentControl's Content is bound eagerly from app startup, same as every
        // other screen), the size never actually *changes* when it's shown, so SizeChanged never
        // fires - leaving BookReaderScreenViewModel.RecomputeCurrentPage stuck behind its
        // viewport-not-yet-known guard and the reader blank. Loaded fires whenever this control is
        // attached and laid out, regardless of whether its size changed, so it closes that gap
        // without needing to know which of Avalonia's measure-while-hidden behaviors is in play.
        Loaded += OnLoaded;
    }

    /// <summary>Pixels from the top within which a pointer move is treated as "near the top edge" for chrome auto-hide reveal (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-design.md) - roughly the height of the top chrome bar itself.</summary>
    private const double AutoHideRevealZonePixels = 60;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        PushViewportSize();
        RootGrid.Focus();
    }

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is BookReaderScreenViewModel vm)
        {
            bool nearTopEdge = e.GetPosition(RootGrid).Y < AutoHideRevealZonePixels;
            vm.NotifyPointerActivity(nearTopEdge);
        }
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is BookReaderScreenViewModel vm)
        {
            vm.NotifyKeyActivity();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => PushViewportSize();

    private void PushViewportSize()
    {
        if (DataContext is BookReaderScreenViewModel vm && Bounds.Width > 0 && Bounds.Height > 0)
        {
            vm.UpdateViewportSize(Bounds.Size);
        }
    }

    private void OnContentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is BookReaderScreenViewModel vm)
        {
            vm.ToggleChromeCommand.Execute(null);
        }
    }

    /// <summary>Tapping the dimmed backdrop behind any drawer/sheet/overlay closes whichever is open - the other close calls are harmless no-ops for the ones that aren't.</summary>
    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is BookReaderScreenViewModel vm)
        {
            vm.CloseTocCommand.Execute(null);
            vm.CloseFontSheetCommand.Execute(null);
            vm.CloseBookmarksCommand.Execute(null);
            vm.CloseHighlightsCommand.Execute(null);
            vm.CloseSearchCommand.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>
    /// A <see cref="ParagraphView"/> drag-selection completed (docs/superpowers/specs/2026-09-01-
    /// books-reader-ergonomics-and-annotations-design.md §"Highlight selection UX"). Translates the
    /// event's paragraph-local bounds into <c>RootGrid</c>'s coordinate space - the one piece of
    /// visual-tree-specific work the view model itself can't do - then hands off the rest to
    /// <see cref="BookReaderScreenViewModel.OnParagraphSelectionCompleted"/>.
    /// </summary>
    private void OnParagraphSelectionCompleted(object? sender, ParagraphSelectionEventArgs e)
    {
        if (DataContext is not BookReaderScreenViewModel vm || e.Source is not ParagraphView source
            || source.DataContext is not BookParagraphDisplay paragraph)
        {
            return;
        }

        var anchor = TranslateToRootGrid(source, e.Bounds);
        vm.OnParagraphSelectionCompleted(paragraph, e.Start, e.End, anchor);
    }

    private void OnParagraphHighlightTapped(object? sender, ParagraphHighlightTappedEventArgs e)
    {
        if (DataContext is not BookReaderScreenViewModel vm || e.Source is not ParagraphView source)
        {
            return;
        }

        var anchor = TranslateToRootGrid(source, e.Bounds);
        vm.OnParagraphHighlightTapped(e.Highlight, anchor);
    }

    private Rect TranslateToRootGrid(Visual source, Rect boundsInSource)
    {
        var topLeft = source.TranslatePoint(boundsInSource.TopLeft, RootGrid) ?? boundsInSource.TopLeft;
        return new Rect(topLeft, boundsInSource.Size);
    }
}
