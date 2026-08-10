namespace Paperbunkr.Data.Entities;

/// <summary>
/// A prose-novel series (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §2) —
/// deliberately independent of the comic-schema <see cref="Series"/>, not a shared/reused table.
/// No CE equivalent; ComicRackCE has no prose-reading concept to port from (see the design spec's
/// CE-verification note).
/// </summary>
public class BookSeries
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? SortName { get; set; }

    /// <summary>Series-level author, e.g. a trilogy by one writer. A <see cref="Book"/> also carries its own Author.</summary>
    public string? Author { get; set; }

    public List<Book> Books { get; set; } = new();
}
