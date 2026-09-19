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
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

public partial class LibraryScreen : UserControl
{
    private readonly TypeAheadSearch.Buffer _typeAheadBuffer = new();

    public LibraryScreen()
    {
        InitializeComponent();
        // Tunnel so Escape closes the Add-issue overlay even while a field inside it has focus
        // (the series-name SuggestBox otherwise swallows Escape to close its own dropdown).
        AddHandler(KeyDownEvent, OnLibraryScreenKeyDown, RoutingStrategies.Tunnel);
        // Type-ahead (docs/superpowers/specs/2026-09-12-grid-typeahead-rangeselect-quit-design.md) -
        // Tunnel from the screen root rather than per-template, so it works regardless of which of
        // the 5 view modes is currently active, same "resolve the real ItemsControl from e.Source"
        // idiom OnLibraryScreenKeyDown's own "/" handling already uses.
        AddHandler(TextInputEvent, OnLibraryScreenTextInput, RoutingStrategies.Tunnel);
        Toolbar.FocusGridRequested += (_, _) => FocusFirstGridItem();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Tells the grid cover pipeline this window's render scaling, so a card bound before it is attached still picks the right decode-size bucket (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md §3.1).</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AsyncCoverImage.NoteRenderScaling(TopLevel.GetTopLevel(this)?.RenderScaling);
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
            const double previewColumnMinWidth = 260;

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
        }
    }

    private void OnLibraryScreenKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not LibraryScreenViewModel vm)
        {
            return;
        }

        // Escape for the Add-issue overlay is now handled centrally in MainViewModel.Escape()
        // (docs/superpowers/specs/2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md's own
        // investigation) - MainWindow's Tunnel KeyDown handler runs before this one and always
        // consumes Escape, so a duplicate check here would never actually be reached.

        // docs/superpowers/specs/2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md - "/"
        // focuses the search box from anywhere in the grid. e.Source (not sender - this fires on the
        // Tunnel pass, before the target's own handlers) is checked so typing "/" inside some other
        // TextBox still types a literal "/" instead of stealing focus.
        if (e.Key == Key.OemQuestion && e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox)
        {
            Toolbar.FocusSearchBox();
            e.Handled = true;
            return;
        }

        // Real accelerator for the context menu's "Edit Properties… (Ctrl+I)" hint (CE parity -
        // miProperties.ShortcutKeys), plus Select All / Delete (docs/superpowers/specs/2026-08-31-
        // app-wide-and-library-keyboard-shortcuts-design.md). Wired here as a plain KeyDown handler,
        // not <UserControl.KeyBindings>, matching PageCanvas's/this file's own already-proven Escape/
        // "/" pattern above. Ctrl+I dispatches through BulkEditCurrentSelectionCommand, not
        // BulkEditSelectionCommand directly - the latter only ever reads issue-granularity
        // Selection.SelectedIds, so pressing Ctrl+I with a real series selected (SeriesSelection)
        // silently no-opped, a genuine pre-existing bug found via live diagnostic logging this
        // session, unrelated to key routing itself. e.Source, not sender, so typing into a TextBox
        // (e.g. the search box) never steals these.
        if (e.Source is TextBox)
        {
            return;
        }

        if (e.Key == Key.I && e.KeyModifiers == KeyModifiers.Control)
        {
            vm.BulkEditCurrentSelectionCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
        {
            vm.SelectAllVisibleCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            vm.DeleteCurrentSelectionCommand.Execute(null);
            e.Handled = true;
        }
        // Type-ahead backspace - Avalonia's TextInput event doesn't fire for Backspace (unlike
        // WinForms KeyPress, which is why CE's own KeySearch could treat it as just another
        // character); handled here instead, sharing the same Buffer/matching logic.
        else if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None)
        {
            if (HandleTypeAhead('\b', e.Source))
            {
                e.Handled = true;
            }
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

    /// <summary>
    /// Return-focus target for <see cref="LibraryToolbar.FocusGridRequested"/> (Esc-with-text in the
    /// search box, see <see cref="LibraryToolbar.axaml.cs"/>'s own doc comment). Finds whichever of
    /// the 4 grid-family <see cref="ItemsControl"/>s is actually visible right now - same "only one
    /// is ever real at a time" fact <see cref="OnCardKeyDown"/> above already documents - and focuses
    /// its first realized item, rather than assuming a specific named control.
    /// </summary>
    private void FocusFirstGridItem()
    {
        var itemsControl = this.GetVisualDescendants()
            .OfType<ItemsControl>()
            .FirstOrDefault(ic => ic.IsEffectivelyVisible && ic.ItemCount > 0);

        if (itemsControl?.ContainerFromIndex(0) is not Control container)
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

    private static Image? FindDogEarPeekImage(Border coverBorder) =>
        (coverBorder.Child as Grid)?.Children.OfType<Image>()
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
        double offsetY = targetRow * (cardHeight + margin);

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, offsetY);
    }

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
        int topRow = (int)(scrollViewer.Offset.Y / (cardHeight + margin));
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
            string name = selectName(items[i]).TrimStart();
            char first = name.Length > 0 ? char.ToUpperInvariant(name[0]) : '\0';
            bool matches = letter == "#" ? !char.IsAsciiLetter(first) : first == letter[0];
            if (matches)
            {
                return i;
            }
        }

        return -1;
    }
}
