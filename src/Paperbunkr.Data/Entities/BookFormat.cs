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
}
