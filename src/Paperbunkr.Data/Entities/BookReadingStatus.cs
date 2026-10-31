namespace Paperbunkr.Data.Entities;

/// <summary>
/// A book's personal reading state (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §3.2). Stored by name.
/// Separate from <see cref="ReadingStatus"/>, which is a comic <see cref="Series"/>' reading-progress relationship.
/// </summary>
public enum BookReadingStatus
{
    Unread = 0,
    WantToRead,
    Reading,
    Finished,
    Abandoned,
}
