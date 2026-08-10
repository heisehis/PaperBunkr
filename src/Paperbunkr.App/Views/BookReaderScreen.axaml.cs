using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    private void OnLoaded(object? sender, RoutedEventArgs e) => PushViewportSize();

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
            vm.CloseSearchCommand.Execute(null);
        }

        e.Handled = true;
    }
}
