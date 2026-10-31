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

    /// <summary>The owning <see cref="BookSeries"/> id when grouping by Series and this is a real
    /// series section (not the "Standalone" bucket); null otherwise. Drives whether the section
    /// header navigates to that series' Book Details view (docs/superpowers/specs/2026-08-27-book-
    /// details-screen-design.md).</summary>
    public int? BookSeriesId { get; init; }

    public required ObservableCollection<BookCardSample> Items { get; init; }

    /// <summary>The header's count (books redesign §3.4): "12", or for a series with gaps "3 of 4 · 1 missing". Read when the header is
    /// built; groups are rebuilt rather than edited in place.</summary>
    public string CountLabel
    {
        get
        {
            int ghosts = 0;
            foreach (var item in Items)
            {
                if (item.IsGhost)
                {
                    ghosts++;
                }
            }

            int owned = Items.Count - ghosts;
            return ghosts == 0 ? owned.ToString(System.Globalization.CultureInfo.CurrentCulture) : $"{owned} of {Items.Count} · {ghosts} missing";
        }
    }

    public bool IsSeries => BookSeriesId is not null;
}
