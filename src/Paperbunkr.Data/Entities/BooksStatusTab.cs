namespace Paperbunkr.Data.Entities;

/// <summary>The status tab strip on the Books screen (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §2). Stored by name.</summary>
public enum BooksStatusTab
{
    All,
    Reading,
    WantToRead,
    Unread,
    Finished,
    Abandoned,
}
