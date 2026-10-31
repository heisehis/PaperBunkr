namespace Paperbunkr.Data.Entities;

/// <summary>Where a book field's current value came from.</summary>
public enum BookFieldSourceKind
{
    File,
    OpenLibrary,
    GoogleBooks,
    Manual,

    /// <summary>Imported from a Calibre library (docs/superpowers/specs/2026-10-27-books-slice4a-calibre-import-design.md).</summary>
    Calibre,

    /// <summary>Taken from an OPDS catalog entry (docs/superpowers/specs/2026-10-27-books-slice4c-opds-design.md).</summary>
    Opds,
}

/// <summary>
/// Provenance of one <see cref="Book"/> field (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §3.4):
/// one row per (book, field), written only when the scanner, a provider or a user edit sets that field. A field with no
/// row is "unknown". A <see cref="BookFieldSourceKind.Manual"/> row is never overwritten by a file refresh or a provider.
/// </summary>
public class BookFieldSource
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public Book? Book { get; set; }

    /// <summary>Property name on <see cref="Book"/> (e.g. "Publisher"), or "Tags" / "Genre" for the tag sets.</summary>
    public string Field { get; set; } = string.Empty;

    public BookFieldSourceKind Source { get; set; }

    public DateTime FetchedUtc { get; set; }

    /// <summary>Provider record id/URL, so Re-fetch can ask the same record again. Null for File and Manual.</summary>
    public string? SourceRef { get; set; }
}
