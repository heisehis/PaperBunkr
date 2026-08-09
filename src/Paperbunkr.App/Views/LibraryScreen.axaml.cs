using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Views;

public partial class LibraryScreen : UserControl
{
    public LibraryScreen()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Spatial arrow-key navigation across the grid-family display modes (P5 follow-up,
    /// docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md), extended
    /// to all grid-family modes in docs/superpowers/specs/2026-08-09-library-toolbar-design.md
    /// Phase A. Walks up to the button's own containing <see cref="ItemsControl"/> rather than a
    /// hardcoded name - 4 different grid-family ItemsControls can now be the one actually visible,
    /// and only one of them is ever real at a time. Not wired on List/Details/Tiles - those
    /// list-shaped modes have no 2D spatial layout for Left/Right/Up/Down to mean anything beyond
    /// what Tab order already does.
    /// </summary>
    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Button { DataContext: SeriesCardSample card } button &&
            button.FindAncestorOfType<ItemsControl>() is { } itemsControl &&
            GridKeyboardNavigation.TryHandleArrowKey(itemsControl, card, e.Key))
        {
            e.Handled = true;
        }
    }
}
