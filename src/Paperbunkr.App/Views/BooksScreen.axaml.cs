using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
}
