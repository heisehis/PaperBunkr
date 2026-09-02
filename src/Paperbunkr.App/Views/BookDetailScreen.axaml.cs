using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Views;

/// <summary>
/// Code-behind for <see cref="BookDetailScreen"/>.
/// </summary>
public partial class BookDetailScreen : UserControl
{
    public BookDetailScreen()
    {
        InitializeComponent();
    }

    /// <summary>Spatial arrow-key navigation across the series-mode book-card grid
    /// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md), mirroring
    /// <c>LibraryScreen.axaml.cs</c>'s own <c>OnCardKeyDown</c>.</summary>
    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Button { DataContext: not null } button ||
            button.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
        {
            return;
        }

        if (GridKeyboardNavigation.TryHandleArrowKey(itemsControl, button, e.Key))
        {
            e.Handled = true;
        }
    }
}
