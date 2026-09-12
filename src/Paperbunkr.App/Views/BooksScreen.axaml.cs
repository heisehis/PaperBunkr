using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class BooksScreen : UserControl
{
    private readonly TypeAheadSearch.Buffer _typeAheadBuffer = new();

    public BooksScreen()
    {
        InitializeComponent();

        // Feed shift-key state to the VM just before a card's CardClickCommand fires, so
        // TileSelectionController can range-extend (docs/superpowers/specs/2026-08-27-books-bulk-
        // series-editing-design.md). Tunnel so this ancestor sees the press before the card Button.
        ContentGrid.AddHandler(PointerPressedEvent, OnContentPointerPressed, RoutingStrategies.Tunnel);
        // Type-ahead (docs/superpowers/specs/2026-09-12-grid-typeahead-rangeselect-quit-design.md).
        ContentGrid.AddHandler(TextInputEvent, OnContentTextInput, RoutingStrategies.Tunnel);
        ContentGrid.AddHandler(KeyDownEvent, OnContentKeyDownForBackspace, RoutingStrategies.Tunnel);
    }

    private bool HandleTypeAhead(char typedChar, object? source)
    {
        if (DataContext is not BooksScreenViewModel vm || source is not Control control ||
            control.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
        {
            return false;
        }

        return TypeAheadSearch.TryHandleTextInput<BookCardSample>(
            _typeAheadBuffer, typedChar, itemsControl, card => card.Title,
            () => vm.ClearSelectionCommand.Execute(null));
    }

    private void OnContentTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source is TextBox || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        bool handled = false;
        foreach (char c in e.Text)
        {
            handled = HandleTypeAhead(c, e.Source);
        }

        if (handled)
        {
            e.Handled = true;
        }
    }

    /// <summary>Type-ahead backspace - see <see cref="LibraryScreen.OnLibraryScreenKeyDown"/>'s
    /// identical doc comment on why this needs KeyDown, not TextInput.</summary>
    private void OnContentKeyDownForBackspace(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox && HandleTypeAhead('\b', e.Source))
        {
            e.Handled = true;
        }
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

        Action<object>? extendSelection = null;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && DataContext is BooksScreenViewModel vm)
        {
            extendSelection = target =>
            {
                if (target is BookCardSample card)
                {
                    vm.ToggleBookSelection(card, isShiftHeld: true);
                }
            };
        }

        if (GridKeyboardNavigation.TryHandleArrowKey(itemsControl, button, e.Key, extendSelection))
        {
            e.Handled = true;
        }
    }
}
