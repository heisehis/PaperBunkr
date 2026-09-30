using System;
using System.Linq;
using Avalonia;
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

    /// <summary>Puts focus back on a book card whenever it should have some and doesn't - grouping, sort, search and workspace switches
    /// all reset the flat or grouped card collections in place while the screen stays attached
    /// (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md). See <see cref="FocusReclaimer"/>.</summary>
    private readonly FocusReclaimer _focus;

    private ItemsControl? _lastCardList;
    private int _lastCardIndex;

    public BooksScreen()
    {
        InitializeComponent();
        _focus = new FocusReclaimer(this, () => ActiveCardList() is not null, FocusFallback);
        DataContextChanged += OnDataContextChanged;
        AddHandler(GotFocusEvent, (_, _) =>
        {
            foreach (var list in CardLists())
            {
                if (VirtualizedFocus.FocusedIndex(list) is var index and >= 0)
                {
                    _lastCardList = list;
                    _lastCardIndex = index;
                    return;
                }
            }
        });
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                _focus.Reclaim();
            }
        };

        // Feed shift-key state to the VM just before a card's CardClickCommand fires, so
        // TileSelectionController can range-extend (docs/superpowers/specs/2026-08-27-books-bulk-
        // series-editing-design.md). Tunnel so this ancestor sees the press before the card Button.
        ContentGrid.AddHandler(PointerPressedEvent, OnContentPointerPressed, RoutingStrategies.Tunnel);
        // Type-ahead (docs/superpowers/specs/2026-09-12-grid-typeahead-rangeselect-quit-design.md).
        ContentGrid.AddHandler(TextInputEvent, OnContentTextInput, RoutingStrategies.Tunnel);
        ContentGrid.AddHandler(KeyDownEvent, OnContentKeyDownForBackspace, RoutingStrategies.Tunnel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _focus.Reclaim();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not BooksScreenViewModel vm)
        {
            return;
        }

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(BooksScreenViewModel.IsGrouped) or nameof(BooksScreenViewModel.GroupField))
            {
                _focus.ReclaimIfFocusWithinOrNowhere();
            }
        };
        vm.Books.CollectionChanged += (_, _) => _focus.ReclaimIfFocusWithinOrNowhere();
        vm.Groups.CollectionChanged += (_, _) => _focus.ReclaimIfFocusWithinOrNowhere();
    }

    /// <summary>The effectively-visible card lists: the flat list, or one inner list per group. Filtered on the item type so the outer
    /// Groups control (items are groups) is excluded.</summary>
    private System.Collections.Generic.IEnumerable<ItemsControl> CardLists() =>
        this.GetVisualDescendants().OfType<ItemsControl>()
            .Where(l => l.IsEffectivelyVisible && l.ItemCount > 0 && l.Items[0] is BookCardSample);

    private ItemsControl? ActiveCardList() => CardLists().FirstOrDefault();

    private void FocusFallback()
    {
        var remembered = _lastCardList is { } last && CardLists().Contains(last) ? last : null;
        var list = remembered ?? ActiveCardList();
        if (list is not null)
        {
            VirtualizedFocus.FocusIndex(list, remembered is null ? 0 : _lastCardIndex, 1);
        }
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
