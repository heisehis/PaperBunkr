using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class BooksScreen : UserControl
{
    public BooksScreen()
    {
        InitializeComponent();

        // Feed shift-key state to the VM just before a card's CardClickCommand fires, so
        // TileSelectionController can range-extend (docs/superpowers/specs/2026-08-27-books-bulk-
        // series-editing-design.md). Tunnel so this ancestor sees the press before the card Button.
        ContentGrid.AddHandler(PointerPressedEvent, OnContentPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnContentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is BooksScreenViewModel vm)
        {
            vm.SetShiftHeld(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
    }

    /// <summary>Spatial arrow-key navigation across the Books grid (docs/superpowers/specs/
    /// 2026-08-31-keyboard-operability-design.md), mirroring <c>LibraryScreen.axaml.cs</c>'s own
    /// <c>OnCardKeyDown</c> - this handler is only ever attached to <c>BookCardTemplate</c>'s card
    /// Button, so unlike Library's version there's no target-type gate needed.</summary>
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
