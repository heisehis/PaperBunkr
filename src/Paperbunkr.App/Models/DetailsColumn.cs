using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// One column in Library's configurable Details table (docs/superpowers/specs/2026-08-27-library-
/// browsing-4b-toolbar-rework-design.md §8). <see cref="IsVisible"/> is a live
/// <see cref="ObservableProperty"/> so the right-click "Columns" header menu can toggle it and the
/// table rebuilds; <see cref="LibraryScreenViewModel"/> persists the visible set (in list order)
/// into <see cref="AppSettings.LibraryDetailsColumns"/>.
/// </summary>
public sealed partial class DetailsColumn : ObservableObject
{
    public required IssueListSortField Field { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Fixed pixel width for this column's header cell and every data cell - a hidden
    /// column (<see cref="IsVisible"/> false) is dropped from the horizontal <c>StackPanel</c>
    /// entirely, so widths only need to line up, not collapse.</summary>
    public double Width { get; init; } = 150;

    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Self-contained toggle for the header's right-click column picker
    /// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - the original dead
    /// <c>ContextMenu</c> two-way bound each checkbox's <c>IsChecked</c> directly to
    /// <see cref="IsVisible"/> with <c>StaysOpenOnClick</c>; <c>ContextMenuEntry</c>-based menus are
    /// rebuilt fresh per right-click and close on each click (no stays-open equivalent in the shared
    /// mechanism), so toggling several columns now takes several right-clicks instead of one -
    /// a deliberate, acknowledged simplification, not an oversight.</summary>
    [RelayCommand]
    private void ToggleVisible() => IsVisible = !IsVisible;
}
