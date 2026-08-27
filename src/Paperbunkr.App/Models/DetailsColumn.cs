using CommunityToolkit.Mvvm.ComponentModel;
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
}
