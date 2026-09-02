namespace Paperbunkr.Data.Entities;

/// <summary>
/// Source file format for a <see cref="Book"/> (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §2/§4). Determines which <c>IBookTextSource</c>
/// implementation (<c>EpubBookSource</c>/<c>PdfBookSource</c>) parses the file.
/// </summary>
public enum BookFormat
{
    Epub,
    Pdf,

    /// <summary>FictionBook 2 (docs/superpowers/specs/2026-09-01-books-format-ingestion-fb2-mobi-design.md). Also covers <c>.fb2.zip</c>-wrapped files.</summary>
    Fb2,

    /// <summary>MOBI/AZW3/AZW (docs/superpowers/specs/2026-09-01-books-format-ingestion-fb2-mobi-design.md) - AZW3 shares this tag with MOBI (same PalmDB container family), distinguished at parse time, not by format tag.</summary>
    Mobi,
}
