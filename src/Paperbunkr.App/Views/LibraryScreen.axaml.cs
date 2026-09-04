using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

public partial class LibraryScreen : UserControl
{
    public LibraryScreen()
    {
        InitializeComponent();
        // Tunnel so Escape closes the Add-issue overlay even while a field inside it has focus
        // (an AutoCompleteBox otherwise swallows Escape for its own dropdown).
        AddHandler(KeyDownEvent, OnLibraryScreenKeyDown, RoutingStrategies.Tunnel);
        Toolbar.FocusGridRequested += (_, _) => FocusFirstGridItem();
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
        if ((item is IssueListRow or SeriesCardSample) && GridKeyboardNavigation.TryHandleArrowKey(itemsControl, button, e.Key))
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

        if (vm.ViewMode is LibraryViewMode.List or LibraryViewMode.Details)
        {
            var box = (vm.ViewMode, vm.IsSeriesGranularity) switch
            {
                (LibraryViewMode.List, false) => ListModeIssueBox,
                (LibraryViewMode.List, true) => ListModeSeriesBox,
                (LibraryViewMode.Details, false) => DetailsModeIssueBox,
                (LibraryViewMode.Details, true) => DetailsModeSeriesBox,
                _ => null,
            };
            box?.ScrollIntoView(index);
            return;
        }

        var (scrollViewer, cardWidth, cardHeight, margin) = vm.ViewMode switch
        {
            LibraryViewMode.PanoramaGrid => (PanoramaScrollViewer, vm.PanoramaTileWidth, vm.PanoramaGridItemHeight, 20.0),
            LibraryViewMode.Tiles => (TilesScrollViewer, vm.TilesCardWidth, vm.TilesCardHeight, 14.0),
            _ => (PosterGridScrollViewer, vm.PosterCardWidth, vm.PosterCardHeight, 20.0),
        };

        int itemsPerRow = Math.Max(1, (int)(scrollViewer.Bounds.Width / (cardWidth + margin)));
        int targetRow = index / itemsPerRow;
        double offsetY = targetRow * (cardHeight + margin);

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, offsetY);
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
