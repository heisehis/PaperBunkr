using System.Collections.ObjectModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// One group section in the Library grid when grouping is active (docs/superpowers/specs/
/// 2026-08-09-library-toolbar-design.md Phase C). <see cref="Items"/> is already sorted by
/// whatever <c>LibrarySortField</c> is active - Sort governs order within each group, Group
/// governs the section boundaries.
/// </summary>
public class SeriesCardGroup
{
    public required string Header { get; init; }

    public required ObservableCollection<SeriesCardSample> Items { get; init; }
}
