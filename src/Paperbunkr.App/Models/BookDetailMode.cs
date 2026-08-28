namespace Paperbunkr.App.Models;

/// <summary>
/// The two presentations of the Book Details screen (docs/superpowers/specs/2026-08-27-book-
/// details-screen-design.md). One <see cref="ViewModels.BookDetailScreenViewModel"/> switches
/// between them, mirroring how <see cref="ViewModels.DetailScreenViewModel"/> handles its
/// series-vs-single-issue split.
/// </summary>
public enum BookDetailMode
{
    /// <summary>A single book: cover, metadata, reading progress, summary, chapter list, bookmarks.</summary>
    Book,

    /// <summary>A book series: name/author + the grid of its books, each opening its own <see cref="Book"/> view.</summary>
    Series,
}
