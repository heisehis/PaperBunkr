using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.ViewModels.LibraryActions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

public partial class LibraryScreen : UserControl
{
    private readonly TypeAheadSearch.Buffer _typeAheadBuffer = new();

    /// <summary>Puts focus back in the active grid whenever it should have some and doesn't - view-mode/granularity/grouping switches and
    /// search/filter/sort resets all swap out the active ItemsControl's content in place while LibraryScreen itself stays attached and
    /// visible throughout (docs/superpowers/specs/2026-09-28-keyboard-focus-reclaim-design.md, Phase 1). See <see cref="FocusReclaimer"/>.</summary>
    private readonly FocusReclaimer _focus;

    private int? _lastGridIndex;

    private readonly AttachedInputRegistration _libraryInput;

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _libraryInput.Service;
        set => _libraryInput.Service = value;
    }

    public LibraryScreen()
    {
        InitializeComponent();
        _focus = new FocusReclaimer(this, () => ActiveGridItemsControl() is not null,
            () => VirtualizedFocus.FocusIndex(ActiveGridItemsControl()!, _lastGridIndex ?? 0, 1));
        // Tunnel so Escape closes the Add-issue overlay even while a field inside it has focus
        // (the series-name SuggestBox otherwise swallows Escape to close its own dropdown).
        AddHandler(KeyDownEvent, OnLibraryScreenKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnScreenPointerPressedTunnel, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnScreenCardKeyDownTunnel, RoutingStrategies.Tunnel);
        AttachDetailsHeaderDrag();
        _libraryInput = new AttachedInputRegistration(this, InputScope.Library, OnLibraryInputAction, service: InputServiceLocator.Current, focusRoot: () => this);
        // Type-ahead (docs/superpowers/specs/2026-09-12-grid-typeahead-rangeselect-quit-design.md) -
        // Tunnel from the screen root rather than per-template, so it works regardless of which of
        // the 5 view modes is currently active, same "resolve the real ItemsControl from e.Source"
        // idiom OnLibraryScreenKeyDown's own "/" handling already uses.
        AddHandler(TextInputEvent, OnLibraryScreenTextInput, RoutingStrategies.Tunnel);
        Toolbar.FocusGridRequested += (_, _) => FocusFirstGridItem();
        DataContextChanged += OnDataContextChanged;
        ApplySelectionCheckboxSetting();
        // Any inner ScrollViewer (grids, list boxes) bubbles this - keeps the A-Z rail's current letter in step (cosmetics pitch 2 #26).
        AddHandler(ScrollViewer.ScrollChangedEvent, OnAnyScrollChanged);
        AddHandler(GotFocusEvent, (_, _) =>
        {
            if (ActiveGridItemsControl() is { } active && VirtualizedFocus.FocusedIndex(active) is var index and >= 0)
            {
                _lastGridIndex = index;
            }
        });
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                _focus.Reclaim();
            }
        };
    }

    private string? _railCurrentLetter;

    /// <summary>
    /// Shows <c>PinnedGroupHeader</c> for the group whose own header has scrolled under the top edge of a grouped cover grid
    /// (docs/superpowers/specs/2026-10-04-library-redesign-design.md, Slice 2), and hides it otherwise: ungrouped, a view mode
    /// with no cover grid, at the very top, or in the stretch where the next group's header is about to take over. Cheap on
    /// purpose, since it runs on every scroll change: it only looks at the realized group containers (a handful), never at tiles.
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

        var (header, count) = pinned.Value;
        if (PinnedGroupHeaderText.Text != header)
        {
            PinnedGroupHeaderText.Text = header;
        }

        string countText = count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        if (PinnedGroupHeaderCount.Text != countText)
        {
            PinnedGroupHeaderCount.Text = countText;
        }

        PinnedGroupHeader.IsVisible = true;
    }

    /// <summary>Room the pinned header needs; once less than this of a group is left on screen, the next group's real header takes over.</summary>
    private const double PinnedGroupHeaderHandOff = 44;

    private (string Header, int Count)? FindPinnedGroup()
    {
        ScrollViewer? viewer = PosterGridScrollViewer.IsEffectivelyVisible ? PosterGridScrollViewer
            : PanoramaScrollViewer.IsEffectivelyVisible ? PanoramaScrollViewer
            : null;
        if (viewer is null || viewer.Offset.Y <= 0 || viewer.Content is not Panel host)
        {
            return null;
        }

        // The viewer's content is a Grid of two Panels (issue / series granularity), each holding the ungrouped and the grouped ItemsControl.
        foreach (var granularity in host.Children.OfType<Panel>())
        {
            if (!granularity.IsVisible)
            {
                continue;
            }

            foreach (var groups in granularity.Children.OfType<ItemsControl>())
            {
                if (!groups.IsVisible)
                {
                    continue;
                }

                foreach (var container in groups.GetRealizedContainers())
                {
                    (string Header, int Count)? group = container.DataContext switch
                    {
                        SeriesCardGroup s => (s.Header, s.Items.Count),
                        IssueListRowGroup r => (r.Header, r.Items.Count),
                        _ => null,
                    };
                    if (group is null)
                    {
                        return null; // the ungrouped grid: its containers are tiles
                    }

                    if (container.TranslatePoint(default, viewer) is not { } top)
                    {
                        continue;
                    }

                    if (top.Y < 0 && top.Y + container.Bounds.Height > PinnedGroupHeaderHandOff)
                    {
                        return group;
                    }
                }
            }
        }

        return null;
    }

    private void OnAnyScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdatePinnedGroupHeader();

        if (DataContext is not LibraryScreenViewModel vm || !vm.ShowAlphabetIndex || e.Source is not ScrollViewer scrollViewer)
        {
            return;
        }

        string? letter = CurrentLetterInView(scrollViewer, vm);
        if (letter == _railCurrentLetter)
        {
            return;
        }

        _railCurrentLetter = letter;
        foreach (var button in AlphabetRail.GetVisualDescendants().OfType<Button>())
        {
            button.Classes.Set("current", letter is not null && Equals(button.Tag, letter));
        }
    }

    /// <summary>The rail letter of whatever is at the top of the scrolled view, or null when it can't be told. Grouped views: the first realized group
    /// still on screen. Ungrouped grids: the item at the estimated first visible row (the inverse of <see cref="ScrollToIndexInGrid"/>'s geometry).
    /// Lists: the first realized row.</summary>
    private string? CurrentLetterInView(ScrollViewer scrollViewer, LibraryScreenViewModel vm)
    {
        if (vm.ViewMode is LibraryViewMode.List or LibraryViewMode.DetailsTable)
        {
            var box = (vm.ViewMode, vm.IsSeriesGranularity) switch
            {
                (LibraryViewMode.List, false) => ListModeIssueBox,
                (LibraryViewMode.List, true) => ListModeSeriesBox,
                (LibraryViewMode.DetailsTable, false) => DetailsModeIssueBox,
                (LibraryViewMode.DetailsTable, true) => DetailsModeSeriesBox,
                _ => null,
            };
            if (box?.ItemsPanelRoot is VirtualizingStackPanel panel && box.ItemsSource is System.Collections.IList items
                && panel.FirstRealizedIndex is >= 0 and int first && first < items.Count)
            {
                return AlphabetIndexEntry.LetterForItem(items[first]);
            }

            return null;
        }

        var geometry = GetGridScrollGeometry(vm.GridCoverFit, vm);
        if (!ReferenceEquals(scrollViewer, geometry.ScrollViewer))
        {
            return null; // a scroll from some other viewer (e.g. a popup list) - not the grid the rail drives
        }

        if (vm.IsGrouped)
        {
            foreach (var itemsControl in scrollViewer.GetVisualDescendants().OfType<ItemsControl>())
            {
                if (!itemsControl.IsEffectivelyVisible || itemsControl.ItemsSource is not System.Collections.IList groups || groups.Count == 0
                    || AlphabetIndexEntry.LetterForItem(groups[0]) is null || groups[0] is not (SeriesCardGroup or IssueListRowGroup))
                {
                    continue;
                }

                for (int i = 0; i < groups.Count; i++)
                {
                    if (itemsControl.ContainerFromIndex(i) is Control container
                        && container.TranslatePoint(new Point(0, container.Bounds.Height), scrollViewer) is { Y: > 0 })
                    {
                        return AlphabetIndexEntry.LetterForItem(groups[i]);
                    }
                }
            }

            return null;
        }

        System.Collections.IList flat = vm.IsSeriesGranularity ? vm.Covers : vm.IssueList.Rows;
        if (flat.Count == 0)
        {
            return null;
        }

        int itemsPerRow = Math.Max(1, (int)(scrollViewer.Bounds.Width / (geometry.CardWidth + geometry.Margin)));
        int firstRow = (int)Math.Max(0, (scrollViewer.Offset.Y - ContinueStripHeight(scrollViewer)) / (geometry.CardHeight + geometry.Margin));
        int index = Math.Clamp(firstRow * itemsPerRow, 0, flat.Count - 1);
        return AlphabetIndexEntry.LetterForItem(flat[index]);
    }

    /// <summary>Mirrors <see cref="CosmeticThumbnailSettings.ShowSelectionCheckbox"/> onto <c>RootGrid</c>'s
    /// <c>selectionCheckboxOff</c> class (the style in LibraryScreen.axaml keys off it).</summary>
    private void ApplySelectionCheckboxSetting()
        => RootGrid.Classes.Set("selectionCheckboxOff", !CosmeticThumbnailSettings.ShowSelectionCheckbox);

    /// <summary>Tells the grid cover pipeline this window's render scaling, so a card bound before it is attached still picks the right decode-size bucket (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md §3.1).</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AsyncCoverImage.NoteRenderScaling(TopLevel.GetTopLevel(this)?.RenderScaling);
        CosmeticThumbnailSettings.OverlaySettingsChanged += ApplySelectionCheckboxSetting;
        ApplySelectionCheckboxSetting();
        _focus.Reclaim();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CosmeticThumbnailSettings.OverlaySettingsChanged -= ApplySelectionCheckboxSetting;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Wires <see cref="LibraryScreenViewModel.ScrollToIndexRequested"/> (docs/superpowers/
    /// specs/2026-09-04-navigation-transition-system-design.md's back-trip realization) to the same
    /// view-mode-aware scroll dispatch <see cref="OnAlphabetIndexLetterClick"/> already uses. No
    /// unsubscribe guard needed - <c>Library</c> is a single stable instance for this control's whole
    /// lifetime (owned once by <c>MainViewModel</c>), matching <c>MainWindow.axaml.cs</c>'s own
    /// <c>OnDataContextChanged</c> precedent.</summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is LibraryScreenViewModel vm)
        {
            vm.ScrollToIndexRequested += index => ScrollToIndex(index, vm);

            // A new search result set starts at its first row (docs/superpowers/specs/2026-09-19-
            // library-search-perf-design.md §5). Index 0 through the same view-mode-aware dispatch:
            // ScrollIntoView(0) for the List/Details boxes, Offset.Y = 0 for the wrapping grids.
            // Sort/group/filter swaps never raise this, so they keep their scroll offset.
            vm.ScrollToTopRequested += () => ScrollToIndex(0, vm);

            // Live preview panel width (docs/superpowers/specs/2026-09-14-library-visual-redesign-
            // design.md §4) - a GridLength can't bind directly to a double VM property, so the
            // column's initial width is set here once and persisted back on every drag. No debounce:
            // matches this ViewModel's own no-debounce philosophy for other immediate-write settings.
            // The column is a fixed pixel width, not Auto, specifically so GridSplitter can resize
            // it - but that means IsVisible="False" on the panel's own content does NOT shrink the
            // column (ColumnDefinition has no IsVisible at all); the column has to be collapsed to
            // GridLength(0) explicitly whenever ShowPreviewPanelColumn goes false, and restored to
            // the real width when it goes true, or hiding the panel leaves dead reserved space
            // (real bug caught on-screen, not just in review).
            var previewColumn = RootGrid.ColumnDefinitions[2];
            const double previewColumnMinWidth = 280;

            void SyncPreviewColumnWidth()
            {
                // MinWidth is a hard layout constraint independent of Width - leaving it at 260
                // while setting Width to 0 still renders the column at 260px (the real bug behind
                // the "hiding the panel leaves dead space" report: Width alone doesn't override it).
                // Zero it out too when hiding, restore it when showing.
                previewColumn.MinWidth = vm.ShowPreviewPanelColumn ? previewColumnMinWidth : 0;
                previewColumn.Width = vm.ShowPreviewPanelColumn
                    ? new GridLength(vm.LibraryPreviewPanelWidth)
                    : new GridLength(0);
            }

            SyncPreviewColumnWidth();
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.ShowPreviewPanelColumn))
                {
                    SyncPreviewColumnWidth();
                }

                // A view-mode/granularity switch swaps out the active grid's content in place while
                // LibraryScreen stays visible throughout - nothing else puts focus back (docs/
                // superpowers/specs/2026-09-28-keyboard-focus-reclaim-design.md). Grouping is a
                // separate subscription below - LibraryScreenViewModel.IsGrouped is a pure alias over
                // IssueList.IsGrouped and never raises its own PropertyChanged.
                if (args.PropertyName is nameof(vm.ViewMode) or nameof(vm.GridCoverFit) or nameof(vm.Granularity))
                {
                    _focus.Reclaim();
                }
            };
            previewColumn.PropertyChanged += (_, args) =>
            {
                if (args.Property == ColumnDefinition.WidthProperty && previewColumn.Width.Value > 0)
                {
                    vm.LibraryPreviewPanelWidth = previewColumn.Width.Value;
                }
            };

            // Scroll-position preservation across a GridCoverFit flip (docs/superpowers/specs/
            // 2026-09-14-library-visual-redesign-design.md §2/§9) - Changing fires while the OLD
            // cover fit's ScrollViewer is still the visible one; Changed fires after the switch, once
            // the NEW one is visible (deferred a tick so its layout pass has actually run before
            // setting Offset against it).
            vm.GridCoverFitChanging += () => CaptureGridScrollPosition(vm.GridCoverFit, vm);
            vm.GridCoverFitChanged += () => Dispatcher.UIThread.Post(() => RestoreGridScrollPosition(vm.GridCoverFit, vm));

            // Grouping - see the comment above on why this can't ride the vm.PropertyChanged handler above.
            vm.IssueList.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(IssueListScreenViewModel.IsGrouped))
                {
                    _focus.Reclaim();
                }
            };

            // A search/filter/sort reset swaps the active collection's content in place - the container that held focus is detached along
            // with the old content. Library is a single stable instance for LibraryScreen's whole lifetime (this method's own doc comment),
            // and these five collections are stable { get; }-only properties for the VM's lifetime too, so one subscription each is enough -
            // no unsubscribe needed, matching every other subscription in this block.
            vm.Covers.CollectionChanged += (_, _) => _focus.Reclaim();
            vm.Groups.CollectionChanged += (_, _) => _focus.Reclaim();
            // A swap can leave the pinned header naming a group that is gone; re-evaluate once the new content has laid out.
            vm.Groups.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(UpdatePinnedGroupHeader, DispatcherPriority.Background);
            vm.IssueList.Groups.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(UpdatePinnedGroupHeader, DispatcherPriority.Background);
            vm.FlatCovers.CollectionChanged += (_, _) => _focus.Reclaim();
            vm.IssueList.Rows.CollectionChanged += (_, _) => _focus.Reclaim();
            vm.IssueList.FlatRows.CollectionChanged += (_, _) => _focus.Reclaim();
        }
    }

    /// <summary>
    /// The Library's shortcuts, as input-service actions in the Library scope (docs/superpowers/specs/2026-10-03-input-service-design.md §9): focus the search box (CE's quick search, plus
    /// the long-standing "/"), refresh, toggle the preview panel, select all, delete, and the selection actions that <see cref="LibraryActionCatalog"/> also puts on the right-click menu and
    /// the bar, so the three can't drift apart. Escape never reaches here: it is the global CloseCurrentView action and <c>MainViewModel.Escape()</c> clears the selection last. Typing
    /// in a text box never steals these (the service ignores keyboard actions under a text field), and neither does a dialog or popup that has focus (this registration is dormant then).
    /// </summary>
    private void OnLibraryInputAction(InputActionEventArgs e)
    {
        if (DataContext is not LibraryScreenViewModel vm)
        {
            return;
        }

        switch (e.Action.Id)
        {
            case InputActionIds.FocusSearch:
                Toolbar.FocusSearchBox();
                e.Handled = true;
                return;
            case InputActionIds.ToggleLibraryPreview:
                vm.ToggleLibraryPreviewPanelCommand.Execute(null);
                e.Handled = true;
                return;
            case InputActionIds.ListOptions:
                vm.ListLayouts.ShowListOptionsCommand.Execute(null);
                e.Handled = true;
                return;
            case InputActionIds.SaveListLayout:
                vm.ListLayouts.SaveLayoutAsCommand.Execute(null);
                e.Handled = true;
                return;
            case InputActionIds.EditListLayouts:
                vm.ListLayouts.ShowEditLayoutsCommand.Execute(null);
                e.Handled = true;
                return;
            case InputActionIds.Refresh:
                // Reloading clears and repopulates the very collections the focused tile lives in, so it runs after the key press has finished routing.
                Dispatcher.UIThread.Post(vm.LoadFromDatabase);
                e.Handled = true;
                return;
            case InputActionIds.CloseCurrentView:
                // Esc inside the inspector hands focus back to the grid card it came from (docs/superpowers/specs/2026-10-04-library-redesign-
                // design.md, Keyboard). Anywhere else it is declined, so the shell's own Escape handling runs as before.
                if (ActiveGridItemsControl() is { } grid && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused
                    && PreviewPanel.IsVisualAncestorOf(focused))
                {
                    VirtualizedFocus.FocusIndex(grid, _lastGridIndex ?? 0, 1);
                    e.Handled = true;
                }

                return;
            case InputActionIds.TabNext:
            case InputActionIds.TabPrevious:
                // The lens tabs (All / Reading / Unread / Read) are the Library's tab strip. Deferred like Refresh: changing the lens swaps the collections the focused tile lives in.
                int lensStep = e.Action.Id == InputActionIds.TabNext ? 1 : -1;
                Dispatcher.UIThread.Post(() => TabStrip.Step(Toolbar, lensStep));
                e.Handled = true;
                return;
            case InputActionIds.LibrarySelectAll:
                vm.SelectAllVisibleCommand.Execute(null);
                e.Handled = true;
                return;
            case InputActionIds.LibraryDeleteSelection:
                vm.DeleteCurrentSelectionCommand.Execute(null);
                e.Handled = true;
                return;
        }

        if (PluginInputActions.IsPluginAction(e.Action.Id))
        {
            // Deferred like the other selection actions: a plugin may rewrite the library under the focused tile.
            if (vm.LibraryPluginCommands.Any(c => PluginInputActions.IdFor(c) == e.Action.Id) && vm.SelectionBarIssueIds().Count > 0)
            {
                string pluginActionId = e.Action.Id;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.RunPluginAction(pluginActionId));
                e.Handled = true;
            }

            return;
        }

        if (!LibraryActionCatalog.KeyActions.ContainsKey(e.Action.Id))
        {
            return;
        }

        // Deferred like the bar's buttons: several of these rebuild the grid the focused tile lives in.
        if (new LibraryActionCatalog(vm, _libraryInput.Service).TryGetKeyCommand(e.Action.Id, out var command, out var parameter))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => command!.Execute(parameter));
            e.Handled = true;
        }
        else if (e.Action.Id == InputActionIds.LibraryEdit)
        {
            // Ctrl+I dispatches through BulkEditCurrentSelectionCommand when the catalog has nothing to run: BulkEditSelectionCommand only reads issue-granularity selection, so a real series
            // selection silently no-opped (a pre-existing bug found via live diagnostic logging, docs/superpowers/specs/2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md).
            vm.BulkEditCurrentSelectionCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Type-ahead backspace - Avalonia's TextInput event doesn't fire for Backspace (unlike WinForms KeyPress, which is why CE's own KeySearch could treat it as just another character); handled
    /// here instead, sharing the same Buffer/matching logic. This is control behavior, not a shortcut, so it stays a key handler rather than an input-service action.
    /// </summary>
    private void OnLibraryScreenKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || e.Key != Key.Back || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (HandleTypeAhead('', e.Source))
        {
            e.Handled = true;
        }
    }

    /// <summary>Type-ahead jump (docs/superpowers/specs/2026-09-12-grid-typeahead-rangeselect-quit-
    /// design.md) - resolves <paramref name="source"/>'s ancestor grid the same way arrow-nav does,
    /// dispatches issue-vs-series text selector/clear-selection by the matched item's own type
    /// (mirrors <see cref="OnCardKeyDown"/>'s own dispatch), not <see cref="LibraryScreenViewModel.IsIssueGranularity"/> -
    /// the ancestor grid could in principle host either row type depending on which template is
    /// currently live.</summary>
    private bool HandleTypeAhead(char typedChar, object? source)
    {
        if (DataContext is not LibraryScreenViewModel vm || source is not Control control ||
            control.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
        {
            return false;
        }

        return TypeAheadSearch.TryHandleTextInput<object>(
            _typeAheadBuffer, typedChar, itemsControl,
            item => item switch { IssueListRow row => row.SeriesName, SeriesCardSample card => card.Name, _ => string.Empty },
            () =>
            {
                vm.ClearSelectionCommand.Execute(null);
                vm.ClearSeriesSelectionCommand.Execute(null);
            });
    }

    private void OnLibraryScreenTextInput(object? sender, TextInputEventArgs e)
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

    /// <summary>Finds whichever of the grid-family <see cref="ItemsControl"/>s is actually visible right now - only one ever is at a time,
    /// same fact <see cref="OnCardKeyDown"/> below already documents. Shared by <see cref="FocusFirstGridItem"/> and <see cref="_focus"/>'s
    /// own fallback, rather than assuming a specific named control (Poster/Panorama/Tiles have ~10 unnamed nested ItemsControls between
    /// view mode, granularity and grouping - this avoids needing to name and branch through every one of them).
    /// <para>
    /// Real bug, found writing the Phase 1 headless tests (docs/superpowers/specs/2026-09-28-keyboard-focus-reclaim-design.md): Details
    /// mode's own column-header row is <em>also</em> an <see cref="ItemsControl"/> (over <c>DetailsColumns</c>), and it sits before the
    /// actual data grid in document order - a plain "first visible ItemsControl with items" match picked the header instead, pre-existing
    /// in <see cref="FocusFirstGridItem"/> too (this method is its exact prior body). Every real content grid's items are <c>Button.card</c>
    /// (confirmed across every ItemTemplate in this file); the header's own items are <c>Button.detailsHeader</c> - checking the first
    /// realized container actually holds a card excludes it.
    /// </para></summary>
    private ItemsControl? ActiveGridItemsControl() =>
        this.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault(ic =>
            ic.IsEffectivelyVisible && ic.ItemCount > 0 && ic.ContainerFromIndex(0) is { } first
            && (first is Button { Classes: var classes } && classes.Contains("card")
                || first.GetVisualDescendants().OfType<Button>().Any(b => b.Classes.Contains("card"))));

    /// <summary>Return-focus target for <see cref="LibraryToolbar.FocusGridRequested"/> (Esc-with-text in the search box, see
    /// <see cref="LibraryToolbar.axaml.cs"/>'s own doc comment) - focuses <see cref="ActiveGridItemsControl"/>'s first realized item.</summary>
    private void FocusFirstGridItem()
    {
        if (ActiveGridItemsControl()?.ContainerFromIndex(0) is not Control container)
        {
            return;
        }

        // The List/Details ListBox strips its ListBoxItem to a non-focusable ContentPresenter (so
        // the inner Button.card is the only tab stop) - focus that inner control, not the container.
        var target = container.Focusable
            ? container
            : container.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(c => c.Focusable);
        target?.Focus();
    }

    private void OnAddIssueBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is LibraryScreenViewModel vm)
        {
            vm.CloseAddIssueCommand.Execute(null);
        }
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
        if (sender is not Button { DataContext: { } item } button ||
            button.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
        {
            return;
        }

        // Single click now only focuses a card (real user direction: it used to navigate
        // immediately on plain click, which made keyboard-driven grid navigation impossible to
        // use - you'd leave the grid the moment you clicked anything). Double-click opens it (see
        // OnCardDoubleTapped); Enter/Space is the keyboard equivalent, matching what Button's own
        // default Click-on-Enter behavior did before Command was removed from these templates.
        if ((item is IssueListRow or SeriesCardSample) && (e.Key == Key.Enter || e.Key == Key.Space))
        {
            OpenCard(item);
            e.Handled = true;
            return;
        }

        // Two independent card types can occupy this same handler depending on Granularity - see
        // this file's own top doc comment and docs/superpowers/specs/2026-08-18-library-book-
        // centric-redesign-design.md Slice 3's follow-up.
        Action<object>? extendSelection = null;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && DataContext is LibraryScreenViewModel vm)
        {
            extendSelection = target =>
            {
                switch (target)
                {
                    case IssueListRow row:
                        vm.ToggleIssueSelection(row, isShiftHeld: true);
                        break;
                    case SeriesCardSample card:
                        vm.ToggleSeriesSelection(card, isShiftHeld: true);
                        break;
                }
            };
        }

        if ((item is IssueListRow or SeriesCardSample) && GridKeyboardNavigation.TryHandleArrowKey(itemsControl, button, e.Key, extendSelection))
        {
            e.Handled = true;
        }
    }

    /// <summary>Double-click opens the card (see <see cref="OnCardKeyDown"/>'s own doc comment for
    /// why plain single-click no longer does). Shared by both the issue and series poster
    /// templates, same "which type is it" dispatch <see cref="OnCardKeyDown"/> already needs.</summary>
    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Button { DataContext: { } item } && item is IssueListRow or SeriesCardSample)
        {
            OpenCard(item);
        }
    }

    private void OpenCard(object item)
    {
        if (DataContext is not LibraryScreenViewModel vm)
        {
            return;
        }

        switch (item)
        {
            case IssueListRow row:
                vm.IssueList.OpenIssueCommand.Execute(row);
                break;
            case SeriesCardSample card:
                vm.SelectCardCommand.Execute(card);
                break;
        }
    }

    /// <summary>
    /// Live preview panel content source (docs/superpowers/specs/2026-09-14-library-visual-redesign-
    /// design.md §4) - every card, across every view mode including List/Details (whose
    /// <c>ListBoxItem</c> is deliberately non-focusable, per this file's <c>Styles</c> comment, so the
    /// inner <c>Button.card</c> stays the one real focus target), routes through this one handler.
    /// Fires on a plain click's own <see cref="OnTilePointerPressed"/>/<see cref="OnSeriesTilePointerPressed"/>
    /// <c>Focus()</c> call and on arrow-key-driven focus movement alike - both are "the user is
    /// looking at this card now," which is exactly what the panel should reflect.
    /// </summary>
    private void OnCardGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: { } item } || DataContext is not LibraryScreenViewModel vm)
        {
            return;
        }

        // Focusing a grid card always returns the panel to following the grid, even when it is the card already previewed
        // (the property setters below would not fire for an unchanged value).
        vm.ClearPreviewDrill();

        switch (item)
        {
            case IssueListRow row:
                vm.PreviewIssue = row;
                break;
            case SeriesCardSample card:
                vm.PreviewSeries = card;
                break;
        }
    }

    /// <summary>
    /// Ctrl/shift-click multi-selection (docs/superpowers/specs/2026-08-24-library-multiselect-
    /// slice1-design.md §3), plus explicit <c>Focus()</c> on every plain click - real user
    /// direction: single click used to also navigate (the tile's own bound <c>Command</c>), which
    /// made keyboard-driven grid navigation unusable (you'd leave the grid the instant you clicked
    /// anything). <c>Command</c> was removed from the template entirely (double-click/Enter/Space
    /// open it instead - see <see cref="OnCardDoubleTapped"/>/<see cref="OnCardKeyDown"/>), and
    /// without a bound Command a plain Button's own default focus-on-press behavior turned out not
    /// to reliably show the <c>:focus-within</c> glow style either (found via manual testing) - so
    /// focus is now set here explicitly rather than assumed. Mirrors <c>DetailTabs.axaml.cs</c>'s
    /// own PointerPressed-based selection handler, adapted for a Button root instead of a Border.
    /// </summary>
    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Button { DataContext: IssueListRow row } button || DataContext is not LibraryScreenViewModel viewModel)
        {
            return;
        }

        button.Focus();

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        viewModel.ToggleIssueSelection(row, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }

    /// <summary>
    /// Ctrl/Shift-click selection on the tiles. The tile handlers are declared on each <see cref="Button"/> card in XAML, but a Button marks a left press handled in its own class
    /// handler before instance handlers run, so those never saw the click and Ctrl/Shift-click selected nothing. Tunnelling from the screen root runs first; the card's own handler
    /// still does the work (focus, toggle) and, for a modified click, marks the press handled so the Button doesn't also "click".
    /// </summary>
    private void OnScreenPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            e.Source is not Visual source ||
            source.FindAncestorOfType<Button>(includeSelf: true) is not { } button ||
            !button.Classes.Contains("card"))
        {
            return;
        }

        switch (button.DataContext)
        {
            case IssueListRow:
                OnTilePointerPressed(button, e);
                break;
            case SeriesCardSample:
                OnSeriesTilePointerPressed(button, e);
                break;
        }
    }

    /// <summary>
    /// Enter and Space open the focused tile. A Button handles both keys itself before a handler declared in XAML on it runs, and these tiles have no bound Command (a click used to navigate, which
    /// made keyboard grid navigation unusable), so <see cref="OnCardKeyDown"/> never saw them and the keys did nothing. Tunnelling from the screen root runs first and hands them to it.
    /// </summary>
    private void OnScreenCardKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        bool arrow = e.Key is Key.Left or Key.Right or Key.Up or Key.Down;
        if (e.Source is Button { DataContext: IssueListRow or SeriesCardSample } button && button.Classes.Contains("card"))
        {
            if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter or Key.Space)
            {
                OnCardKeyDown(button, e);
            }
            else if (arrow)
            {
                // The grid's own move first; when it has nowhere to go (the last row of a group, the first row on its way up to the toolbar) the screen's directional move takes over. Left to
                // bubble, Avalonia's ItemsControl acts on the key on the way up and drops focus, so the key went nowhere (see Wanted).
                OnCardKeyDown(button, e);
                if (!e.Handled && e.KeyModifiers == KeyModifiers.None)
                {
                    e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
                }
            }

            return;
        }

        // The toolbar, the chips and the sidebar: plain controls with no arrow handling of their own, so Down from the toolbar reaches the grid and Up comes back.
        if (arrow && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
        }
    }

    /// <summary>
    /// <c>DogEarThumbnails</c> hover peek (docs/superpowers/specs/2026-09-13-preferences-cosmetic-
    /// toggles-design.md) - mirrors CE's own hover-only real-page-2 fetch. Not a binding: the decode
    /// is lazy (only when actually hovered) and needs a staleness guard against container recycling
    /// during the async decode, the same concern <see cref="Views.AsyncCoverImage"/> solves with a
    /// generation token - here the simpler "is this Border still showing the same row, and is the
    /// pointer still over it" recheck is enough since there's no eager binding to race against.
    /// </summary>
    /// <summary>~500ms hover delay for <see cref="ShowToolTips"/> (matches Avalonia's own native
    /// <c>ToolTip.ShowDelay</c> default - no existing bespoke hover-tooltip timing precedent in this
    /// codebase to match instead). One shared timer: only one tile can be mid-hover-delay at a time,
    /// since a pointer leaving one tile always fires PointerExited before entering another.</summary>
    private DispatcherTimer? _tooltipDelayTimer;

    private void OnCoverPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Border coverBorder || ResolvePeekRow(coverBorder.DataContext) is not { } row)
        {
            return;
        }

        // Corner-slot precedence (docs/superpowers/specs/2026-09-14-library-visual-redesign-
        // design.md §3): Selection > Dog-ear, checked against the tile's own selection state, not
        // the representative row's (a series card's RepresentativeRow.IsSelected is a per-issue
        // selection flag - a different concept from SeriesCardSample.IsSelected, the actual tile
        // selection for a series-granularity card).
        bool isSelected = coverBorder.DataContext switch
        {
            IssueListRow r => r.IsSelected,
            SeriesCardSample c => c.IsSelected,
            _ => false,
        };

        if (!isSelected)
        {
            TryShowDogEarPeek(coverBorder, row);
        }

        ArmHoverTooltip(coverBorder, row);
    }

    /// <summary>Series-granularity cards (<see cref="SeriesCardSample"/>) carry a full
    /// <see cref="IssueListRow"/> for their cover-representative issue - the same issue
    /// <c>CoverKey</c> is keyed to - so DogEar/tooltip/rating reuse it as-is rather than needing a
    /// second, series-shaped implementation.</summary>
    private static IssueListRow? ResolvePeekRow(object? dataContext) => dataContext switch
    {
        IssueListRow row => row,
        SeriesCardSample card => card.RepresentativeRow,
        _ => null,
    };

    private void TryShowDogEarPeek(Border coverBorder, IssueListRow row)
    {
        if (!CosmeticThumbnailSettings.DogEarThumbnails || !row.DogEarEligible)
        {
            return;
        }

        var peekImage = FindDogEarPeekImage(coverBorder);
        if (peekImage is null)
        {
            // The Poster peek's LazyPart activates from the border's IsPointerOver binding; if that hasn't
            // propagated yet when PointerEntered runs, look again once the current input event has finished.
            Dispatcher.UIThread.Post(() =>
            {
                if (coverBorder.IsPointerOver && ReferenceEquals(ResolvePeekRow(coverBorder.DataContext), row)
                    && FindDogEarPeekImage(coverBorder) is not null)
                {
                    TryShowDogEarPeek(coverBorder, row);
                }
            });
            return;
        }

        string stem = row.CoverKey;
        string filePath = row.FilePath!; // DogEarEligible requires HasFile

        var cached = DogEarThumbnailCache.TryGetCached(stem);
        if (cached is not null)
        {
            peekImage.Source = cached;
            peekImage.IsVisible = true;
            return;
        }

        Task.Run(() => DogEarThumbnailCache.Get(stem, filePath)).ContinueWith(
            t =>
            {
                var decoded = t.IsCompletedSuccessfully ? t.Result : null;
                if (decoded is null)
                {
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (ResolvePeekRow(coverBorder.DataContext) is { } currentRow && currentRow.CoverKey == stem && coverBorder.IsPointerOver)
                    {
                        peekImage.Source = decoded;
                        peekImage.IsVisible = true;
                    }
                });
            },
            TaskScheduler.Default);
    }

    /// <summary>docs/superpowers/specs/2026-09-13-preferences-cosmetic-toggles-design.md - excludes
    /// Tiles view (CE's own <c>ItemViewMode.Tile</c> exclusion); the tooltip only makes sense on the
    /// grid/list templates this handler is wired to anyway (Tiles uses a different template with no
    /// PointerEntered wired to it).</summary>
    private void ArmHoverTooltip(Border coverBorder, IssueListRow row)
    {
        _tooltipDelayTimer?.Stop();

        if (!CosmeticThumbnailSettings.ShowToolTips)
        {
            return;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ResolvePeekRow(coverBorder.DataContext) == row && coverBorder.IsPointerOver)
            {
                ShowHoverTooltip(coverBorder, row);
            }
        };
        _tooltipDelayTimer = timer;
        timer.Start();
    }

    private void ShowHoverTooltip(Border coverBorder, IssueListRow row)
    {
        ComicHoverTooltipCard.DataContext = row;
        ComicHoverTooltipPopup.PlacementTarget = coverBorder;
        ComicHoverTooltipPopup.IsOpen = true;
        // Popup unmounts its content on close, so the entrance Transition never gets a chance to
        // replay on a re-open unless the "open" class is re-added after each open - same reasoning
        // StatusBar.axaml.cs documents for its own peek popover.
        ComicHoverTooltipCard.Classes.Remove("open");
        ComicHoverTooltipCard.Classes.Add("open");
    }

    private void OnCoverPointerExited(object? sender, PointerEventArgs e)
    {
        _tooltipDelayTimer?.Stop();
        ComicHoverTooltipPopup.IsOpen = false;

        if (sender is not Border coverBorder)
        {
            return;
        }

        var peekImage = FindDogEarPeekImage(coverBorder);
        if (peekImage is not null)
        {
            peekImage.IsVisible = false;
        }
    }

    /// <summary>
    /// A cover's hover "More" button (docs/superpowers/specs/2026-10-04-library-redesign-design.md, Slice 2): the tile's own
    /// right-click menu, at the pointer. A mouse-only shortcut - the keyboard and the controller reach the same menu
    /// with the context-menu key, which is why the button is not focusable.
    /// The menu is owned by the screen, not the button: the button exists only while the cover is hovered, so it is removed the
    /// moment the pointer moves onto the menu, and a flyout anchored to it was torn down with it. It also opens a tick later,
    /// after this click has finished routing through a control that is about to be detached.
    /// </summary>
    private void OnTileMoreClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control { DataContext: { } target } || DataContext is not IContextMenuProvider provider)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (provider.BuildContextMenu(target) is { Count: > 0 } entries)
            {
                ContextMenuHost.ShowMenuAtPointer(this, entries);
            }
        });
    }

    /// <summary>The peek Image is a direct child of the cover Grid in the Panorama templates, but inside a
    /// <see cref="Controls.LazyPart"/> in the Poster ones (docs/superpowers/specs/2026-09-19-library-scroll-
    /// smoothness-design.md §4). Looking only at direct children found nothing on Poster cards, so the dog-ear
    /// never showed there - the 2026-09-26 library audit's "I enable it and don't see it work".</summary>
    private static Image? FindDogEarPeekImage(Border coverBorder) =>
        (coverBorder.Child as Grid)?.Children
            .Select(child => child is Controls.LazyPart part ? part.Child : child)
            .OfType<Image>()
            .FirstOrDefault(i => i.Name is "PanoramaDogEarImage" or "PosterDogEarImage"
                or "SeriesPanoramaDogEarImage" or "PosterSeriesDogEarImage");

    /// <summary>Series-granularity counterpart to <see cref="OnTilePointerPressed"/> (docs/superpowers/
    /// specs/2026-08-24-library-multiselect-slice3-design.md) - same explicit-focus-on-click and
    /// ctrl/shift-only selection-toggle gating.</summary>
    private void OnSeriesTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Button { DataContext: SeriesCardSample card } button || DataContext is not LibraryScreenViewModel viewModel)
        {
            return;
        }

        button.Focus();

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        viewModel.ToggleSeriesSelection(card, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }

    /// <summary>
    /// A-Z jump indexer (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase B).
    /// List/Details are virtualized ListBoxes now, so those get a real
    /// <see cref="ListBox.ScrollIntoView(int)"/>; the wrapping grid modes
    /// (Poster/Panorama/Tiles) still estimate a scroll offset from items-per-row against the
    /// active ScrollViewer's width. <c>ShowAlphabetIndex</c> only lights up when ungrouped, so the
    /// flat FlatRows/FlatCovers index equals the plain Rows/Covers index (no interleaved headers).
    /// </summary>
    private void OnAlphabetIndexLetterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string letter } || DataContext is not LibraryScreenViewModel vm)
        {
            return;
        }

        // Grouped by letter (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #26): jump to that
        // letter's group header rather than estimating an offset into a flat list.
        if (vm.IsGrouped)
        {
            ScrollToLetterGroup(letter, vm);
            return;
        }

        // ShowAlphabetIndex only lights up for the granularity whose own sort is Name/Series and
        // ungrouped (see LibraryScreenViewModel.ShowAlphabetIndex) - the other granularity's
        // collection is irrelevant to this click regardless of which one is "active" here.
        int index = vm.IsSeriesGranularity
            ? FindFirstIndexForLetter(vm.Covers, c => c.Name, letter)
            : FindFirstIndexForLetter(vm.IssueList.Rows, r => r.SeriesName, letter);
        if (index < 0)
        {
            return;
        }

        ScrollToIndex(index, vm);
    }

    /// <summary>Scrolls the active grouped view so the group whose header is <paramref name="letter"/> comes into view. Poster/Panorama/
    /// Tiles render each group inside a virtualizing <see cref="ItemsControl"/> of groups, found here as the visible one whose items are
    /// group objects; List/Details use their flattened <see cref="ListBox"/> (header rows are real entries), scrolled by the header's flat index.</summary>
    private void ScrollToLetterGroup(string letter, LibraryScreenViewModel vm)
    {
        if (vm.ViewMode is LibraryViewMode.List or LibraryViewMode.DetailsTable)
        {
            var flat = vm.IsSeriesGranularity ? (IList<object>)vm.FlatCovers : vm.IssueList.FlatRows;
            for (int i = 0; i < flat.Count; i++)
            {
                if (flat[i] is GridSectionHeader header && header.Header == letter)
                {
                    ScrollToIndex(i, vm);
                    return;
                }
            }

            return;
        }

        var scrollViewer = GetGridScrollGeometry(vm.GridCoverFit, vm).ScrollViewer;
        foreach (var itemsControl in scrollViewer.GetVisualDescendants().OfType<ItemsControl>())
        {
            if (!itemsControl.IsEffectivelyVisible || itemsControl.ItemsSource is not System.Collections.IList groups || groups.Count == 0)
            {
                continue;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                string? header = groups[i] switch
                {
                    SeriesCardGroup s => s.Header,
                    IssueListRowGroup r => r.Header,
                    _ => null,
                };
                if (header is null)
                {
                    break; // not a groups control (e.g. the tiles inside a group)
                }

                if (header == letter)
                {
                    itemsControl.ScrollIntoView(i);
                    return;
                }
            }
        }
    }

    /// <summary>The view-mode-aware scroll dispatch shared by <see cref="OnAlphabetIndexLetterClick"/>
    /// and the back-trip cover-morph realization wired in <see cref="OnDataContextChanged"/> (docs/
    /// superpowers/specs/2026-09-04-navigation-transition-system-design.md) - List/DetailsTable get a
    /// real <see cref="ListBox.ScrollIntoView(int)"/>; the wrapping grid modes (Poster/Panorama/Tiles)
    /// estimate a scroll offset from items-per-row against the active ScrollViewer's width. Assumes
    /// <paramref name="index"/> is already into the ungrouped, matching-granularity flat collection -
    /// same assumption <see cref="OnAlphabetIndexLetterClick"/> already made.</summary>
    private void ScrollToIndex(int index, LibraryScreenViewModel vm)
    {
        if (vm.ViewMode is LibraryViewMode.List or LibraryViewMode.DetailsTable)
        {
            var box = (vm.ViewMode, vm.IsSeriesGranularity) switch
            {
                (LibraryViewMode.List, false) => ListModeIssueBox,
                (LibraryViewMode.List, true) => ListModeSeriesBox,
                (LibraryViewMode.DetailsTable, false) => DetailsModeIssueBox,
                (LibraryViewMode.DetailsTable, true) => DetailsModeSeriesBox,
                _ => null,
            };
            box?.ScrollIntoView(index);
            return;
        }

        ScrollToIndexInGrid(vm.GridCoverFit, index, vm);
    }

    /// <summary>Cover-fit-parameterized geometry lookup, factored out of <see cref="ScrollToIndex"/>
    /// so <see cref="CaptureGridScrollPosition"/>/<see cref="RestoreGridScrollPosition"/> (docs/
    /// superpowers/specs/2026-09-14-library-visual-redesign-design.md §2/§9) can query it for an
    /// explicit old/new cover fit, not just <c>vm.GridCoverFit</c>'s current value.</summary>
    private (ScrollViewer ScrollViewer, double CardWidth, double CardHeight, double Margin) GetGridScrollGeometry(
        LibraryGridCoverFit coverFit, LibraryScreenViewModel vm) => coverFit switch
    {
        LibraryGridCoverFit.Panorama => (PanoramaScrollViewer, vm.PanoramaTileWidth, vm.PanoramaGridItemHeight, 10.0),
        LibraryGridCoverFit.Tiles => (TilesScrollViewer, vm.TilesCardWidth, vm.TilesCardHeight, 6.0),
        _ => (PosterGridScrollViewer, vm.PosterCardWidth, vm.PosterCardHeight, 20.0),
    };

    private void ScrollToIndexInGrid(LibraryGridCoverFit coverFit, int index, LibraryScreenViewModel vm)
    {
        var (scrollViewer, cardWidth, cardHeight, margin) = GetGridScrollGeometry(coverFit, vm);
        int itemsPerRow = Math.Max(1, (int)(scrollViewer.Bounds.Width / (cardWidth + margin)));
        int targetRow = index / itemsPerRow;
        double offsetY = ContinueStripHeight(scrollViewer) + (targetRow * (cardHeight + margin));

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, offsetY);
    }

    /// <summary>Height the "Continue reading" strip takes at the top of a cover grid's scroller (0 while hidden), so the row-based scroll estimates above and below stay aligned with the grid.</summary>
    private static double ContinueStripHeight(ScrollViewer scrollViewer) =>
        scrollViewer.Content is Panel host && host.Children.OfType<LibraryContinueStrip>().FirstOrDefault() is { IsVisible: true } strip
            ? strip.Bounds.Height
            : 0;

    /// <summary>Holds the approximate item index scrolled-to just before a
    /// <see cref="LibraryGridCoverFit"/> flip rebuilds the grid with different tile dimensions
    /// (docs/superpowers/specs/2026-09-14-library-visual-redesign-design.md §2/§9) - a transient
    /// field, deliberately not routed through <c>LibraryBrowseHistory</c>/<c>LibraryBrowseState</c>
    /// (<c>LibraryScreenViewModel.cs</c>'s own <c>_browseHistory</c>), which only ever tracks
    /// navigation targets (content type/collection/search), not display settings - folding a
    /// cosmetic toggle's scroll offset in there would make Back/Forward start undoing display
    /// toggles instead of navigating.</summary>
    private int? _pendingGridScrollIndex;

    private void CaptureGridScrollPosition(LibraryGridCoverFit oldCoverFit, LibraryScreenViewModel vm)
    {
        if (vm.ViewMode != LibraryViewMode.PosterGrid)
        {
            return;
        }

        var (scrollViewer, cardWidth, cardHeight, margin) = GetGridScrollGeometry(oldCoverFit, vm);
        int itemsPerRow = Math.Max(1, (int)(scrollViewer.Bounds.Width / (cardWidth + margin)));
        int topRow = (int)Math.Max(0, (scrollViewer.Offset.Y - ContinueStripHeight(scrollViewer)) / (cardHeight + margin));
        _pendingGridScrollIndex = topRow * itemsPerRow;
    }

    private void RestoreGridScrollPosition(LibraryGridCoverFit newCoverFit, LibraryScreenViewModel vm)
    {
        if (_pendingGridScrollIndex is not { } index)
        {
            return;
        }

        _pendingGridScrollIndex = null;
        ScrollToIndexInGrid(newCoverFit, index, vm);
    }

    // --- Drag-and-drop import (docs/superpowers/specs/2026-08-31-drag-and-drop-import-design.md) ---
    // Both handlers stay thin: DragOver just gates on the File format, Drop resolves local paths and
    // hands off to the ViewModel, which owns the service call / reload / toast.

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool enabled = (DataContext as LibraryScreenViewModel)?.DragDropImportEnabled == true;
        e.DragEffects = enabled && e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not LibraryScreenViewModel vm || !vm.DragDropImportEnabled)
        {
            return;
        }

        var paths = DragDropPaths.Extract(e);
        if (paths.Count > 0)
        {
            await vm.ImportDroppedPathsAsync(paths);
        }
    }

    private static int FindFirstIndexForLetter<T>(IReadOnlyList<T> items, Func<T, string> selectName, string letter)
    {
        for (int i = 0; i < items.Count; i++)
        {
            // Same bucket rule as the rail's own letters and the Alphabetical group (skips "The ").
            if (AlphabetIndexEntry.LetterFor(selectName(items[i])) == letter)
            {
                return i;
            }
        }

        return -1;
    }
}
