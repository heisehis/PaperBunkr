using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Views;

public partial class LibraryScreen : UserControl
{
    public LibraryScreen()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Spatial arrow-key navigation across the grid-view series cards (P5 follow-up,
    /// docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md). Wired only
    /// on the grid-view template (<c>CoversList</c>, a <see cref="WrapPanel"/>) - the list-view
    /// template has no 2D spatial layout for Left/Right/Up/Down to mean anything beyond what Tab
    /// order already does.
    /// </summary>
    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Button { DataContext: SeriesCardSample card } &&
            GridKeyboardNavigation.TryHandleArrowKey(CoversList, card, e.Key))
        {
            e.Handled = true;
        }
    }
}
