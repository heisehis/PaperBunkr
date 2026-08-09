using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

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

    /// <summary>
    /// A-Z jump indexer (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase B).
    /// Estimates a scroll offset rather than a real ScrollIntoView - none of the 7 display modes
    /// are backed by a virtualized ListBox, so there's no per-item realized container to scroll to
    /// precisely. For wrapping (grid-family) modes, estimates items-per-row from the active
    /// ScrollViewer's current width; for List/Details (single column), the index maps directly to
    /// a row.
    /// </summary>
    private void OnAlphabetIndexLetterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string letter } || DataContext is not LibraryScreenViewModel vm)
        {
            return;
        }

        int index = FindFirstIndexForLetter(vm.Covers, letter);
        if (index < 0)
        {
            return;
        }

        var (scrollViewer, cardWidth, cardHeight, margin, wraps) = vm.ViewMode switch
        {
            LibraryViewMode.CompactGrid => (CompactScrollViewer, vm.CompactCardWidth, vm.CompactCardHeight, 14.0, true),
            LibraryViewMode.ComfortableGrid => (ComfortableScrollViewer, vm.ComfortableCardWidth, vm.ComfortableCardHeight, 20.0, true),
            LibraryViewMode.CoverOnlyGrid => (CoverOnlyScrollViewer, vm.CoverOnlyCardWidth, vm.CoverOnlyCardHeight, 20.0, true),
            // Panorama's per-item width varies with each cover's own aspect ratio - averaged
            // estimate for items-per-row purposes only, not used for rendering.
            LibraryViewMode.PanoramaGrid => (PanoramaScrollViewer, 215.0, vm.PanoramaCardHeight, 20.0, true),
            LibraryViewMode.Tiles => (TilesScrollViewer, vm.TilesCardWidth, vm.TilesThumbHeight + 20, 14.0, true),
            LibraryViewMode.List => (ListScrollViewer, 0.0, 95.0, 0.0, false),
            LibraryViewMode.Details => (DetailsScrollViewer, 0.0, 64.0, 0.0, false),
            _ => (ComfortableScrollViewer, vm.ComfortableCardWidth, vm.ComfortableCardHeight, 20.0, true),
        };

        int itemsPerRow = wraps ? Math.Max(1, (int)(scrollViewer.Bounds.Width / (cardWidth + margin))) : 1;
        int targetRow = index / itemsPerRow;
        double offsetY = targetRow * (cardHeight + margin);

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, offsetY);
    }

    private static int FindFirstIndexForLetter(IReadOnlyList<SeriesCardSample> covers, string letter)
    {
        for (int i = 0; i < covers.Count; i++)
        {
            string name = covers[i].Name.TrimStart();
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
