using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using System.Collections.Generic;
using Paperbunkr.App.Services.Input;

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

    private readonly AttachedInputRegistration _screenInput;

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _screenInput.Service;
        set => _screenInput.Service = value;
    }

    private bool RunListLayoutCommand(Func<BooksScreenViewModel, System.Windows.Input.ICommand> command)
    {
        if (DataContext is not BooksScreenViewModel vm)
        {
            return false;
        }

        command(vm).Execute(null);
        return true;
    }

    public BooksScreen()
    {
        InitializeComponent();
        _screenInput = ScreenInput.Attach(this, InputScope.Books, new Dictionary<string, Func<bool>>
        {
            [InputActionIds.FocusSearch] = () => ScreenInput.FocusTextBox(this, "BooksSearchBox"),
            [InputActionIds.ListOptions] = () => RunListLayoutCommand(vm => vm.ListLayouts.ShowListOptionsCommand),
            [InputActionIds.SaveListLayout] = () => RunListLayoutCommand(vm => vm.ListLayouts.SaveLayoutAsCommand),
            [InputActionIds.EditListLayouts] = () => RunListLayoutCommand(vm => vm.ListLayouts.ShowEditLayoutsCommand),

            // Reloading rebuilds the collections the focused card lives in, so the shortcuts that change them run after the key press has finished routing.
            [InputActionIds.Refresh] = () => DataContext is BooksScreenViewModel vm && ScreenInput.Deferred(vm.LoadFromDatabase),
            [InputActionIds.BooksSelectAll] = () => DataContext is BooksScreenViewModel vm && ScreenInput.Deferred(() => vm.SelectAllVisibleCommand.Execute(null)),
            [InputActionIds.BooksEditSelection] = () => DataContext is BooksScreenViewModel { HasSelection: true } vm && ScreenInput.Deferred(() => vm.EditSelectionCommand.Execute(null)),
            [InputActionIds.BooksDeleteSelection] = () => DataContext is BooksScreenViewModel { HasSelection: true } vm && ScreenInput.Deferred(() => vm.DeleteSelectionCommand.Execute(null)),
        });
        _focus = new FocusReclaimer(this, () => ActiveCardList() is not null, FocusFallback);
        // Arrows a card did not use (at the edge of its grid, or from the toolbar) move on to the nearest control that way: across the series groups and between the toolbar and the grid.
        KeyDown += (_, e) => e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
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

        // Redesign §3.3-3.5: the pinned group header follows the scroll, and the toolbar sheds labels in narrow windows.
        ContentScroller.ScrollChanged += (_, _) => UpdatePinnedGroupHeader();
        SizeChanged += (_, e) => ApplyWidthClasses(e.NewSize.Width);
    }

    /// <summary>Below this screen width Group / Sort / Shelves drop their labels to icons; below <see cref="CompactWidth"/> the nightstand hides too.</summary>
    public const double NarrowWidth = 1100;

    public const double CompactWidth = 900;

    private void ApplyWidthClasses(double width)
    {
        Classes.Set("narrow", width < NarrowWidth);
        Classes.Set("compact", width < CompactWidth);
    }

    /// <summary>Room the pinned header needs; once less than this of a group is left on screen, the next group's real header takes over.</summary>
    private const double PinnedGroupHeaderHandOff = 44;

    /// <summary>
    /// Shows <c>PinnedGroupHeader</c> for the group whose own header has scrolled under the top of the content (the Library's pattern,
    /// docs/superpowers/specs/2026-10-04-library-redesign-design.md Slice 2). It looks only at the realized group containers, never at cards.
    /// </summary>
    private void UpdatePinnedGroupHeader()
    {
        var pinned = FindPinnedGroup();
        if (pinned is null)
        {
            if (PinnedGroupHeader.IsVisible)
            {
                PinnedGroupHeader.IsVisible = false;
            }

            return;
        }

        if (PinnedGroupHeaderText.Text != pinned.Header)
        {
            PinnedGroupHeaderText.Text = pinned.Header;
        }

        if (PinnedGroupHeaderCount.Text != pinned.CountLabel)
        {
            PinnedGroupHeaderCount.Text = pinned.CountLabel;
        }

        PinnedGroupHeader.IsVisible = true;
    }

    private BookCardGroup? FindPinnedGroup()
    {
        if (ContentScroller.Offset.Y <= 0)
        {
            return null;
        }

        var groups = GroupedGrid.IsEffectivelyVisible ? GroupedGrid : GroupedShelf.IsEffectivelyVisible ? GroupedShelf : null;
        if (groups is null)
        {
            return null;
        }

        foreach (var container in groups.GetRealizedContainers())
        {
            if (container.DataContext is not BookCardGroup group || container.TranslatePoint(default, ContentScroller) is not { } top)
            {
                continue;
            }

            if (top.Y < 0 && top.Y + container.Bounds.Height > PinnedGroupHeaderHandOff)
            {
                return group;
            }
        }

        return null;
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
            .Where(l => l.IsEffectivelyVisible && l.ItemCount > 0 && l.Items[0] is BookCardSample && !ReferenceEquals(l, NightstandList));

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

    /// <summary>A rail letter: scroll the first book (flat list) or group (grouped) filed under it into view. The Books panels are plain wrapping
    /// panels, so the container exists; a dimmed letter (nothing under it) does nothing.</summary>
    private void OnAlphabetIndexLetterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string letter } || DataContext is not BooksScreenViewModel vm)
        {
            return;
        }

        if (vm.IsGrouped)
        {
            // The outer group lists are plain stacks, so every group has a container.
            int group = Services.BooksAlphabet.FirstGroupIndex(vm.Groups, letter);
            if (group >= 0)
            {
                (vm.IsGridView ? GroupedGrid : GroupedShelf).ContainerFromIndex(group)?.BringIntoView();
            }

            return;
        }

        // The flat lists virtualize, so an off-screen book has no container: ScrollIntoView realizes it first.
        int book = Services.BooksAlphabet.FirstBookIndex(vm.Books, vm.SortField, letter);
        if (book >= 0)
        {
            (vm.IsGridView ? FlatGrid : FlatShelf).ScrollIntoView(book);
        }
    }

    /// <summary>Spatial arrow-key navigation across the Books grid (docs/superpowers/specs/
    /// 2026-08-31-keyboard-operability-design.md), mirroring <c>LibraryScreen.axaml.cs</c>'s own
    /// <c>OnCardKeyDown</c> - this handler is only ever attached to <c>BookCardTemplate</c>'s card
    /// Button, so unlike Library's version there's no target-type gate needed.</summary>
    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        // A spine is the Button itself; a cover card (BookCoverCard) wraps its Button, which the key bubbled up from.
        var button = sender as Button
            ?? (sender as Control)?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("card"));
        if (button is not { DataContext: not null } || button.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
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
