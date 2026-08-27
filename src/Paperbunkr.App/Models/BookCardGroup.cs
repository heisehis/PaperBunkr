using System.Collections.ObjectModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// One group section in the Books grid when grouping by Series or Author is active (docs/
/// superpowers/specs/2026-08-27-books-screen-chrome-and-home-strip-design.md). Mirrors
/// <see cref="SeriesCardGroup"/>: <see cref="Items"/> is already in the active sort order, Sort
/// governs order within each group, Group governs the section boundaries.
/// </summary>
public sealed class BookCardGroup
{
    public required string Header { get; init; }

    public required ObservableCollection<BookCardSample> Items { get; init; }
}
