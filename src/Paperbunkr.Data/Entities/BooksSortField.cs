namespace Paperbunkr.Data.Entities;

/// <summary>
/// Sort dimensions for the Books screen (docs/superpowers/specs/2026-08-27-books-screen-chrome-and-
/// home-strip-design.md). Deliberately short - "books don't need the comic chrome" - so no
/// per-field catalog like <c>IssueListFieldCatalog</c>, just this enum + a switch in the ViewModel.
/// </summary>
public enum BooksSortField
{
    Title,
    Author,
    RecentlyAdded,
    LastOpened,

    // Books slice 2a (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §3). Stored by name; books without a value sort last.
    Rating,
    Progress,
    Publisher,
    Published,
    SeriesOrder,
}
