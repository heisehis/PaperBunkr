namespace Paperbunkr.Data.Entities;

/// <summary>Which flat field a book tag belongs to, mirroring <see cref="IssueTagField"/>.</summary>
public enum BookTagField
{
    Genre,
    Tags,
}

/// <summary>
/// One tag value on a <see cref="Book"/> (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §3.3).
/// Mirrors <see cref="IssueTag"/> without the weight: weights are a comics idea. Unique per (book, field, value), case-insensitive.
/// </summary>
public class BookTag
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public Book? Book { get; set; }

    public BookTagField Field { get; set; }

    public string Value { get; set; } = string.Empty;

    /// <summary>Free text; null renders as "Uncategorized".</summary>
    public string? Category { get; set; }
}
