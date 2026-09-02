using System.Collections.Generic;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Models;

/// <summary>
/// One entry in <c>BookReaderScreenViewModel.CurrentPageParagraphs</c> - wraps the raw parsed
/// <see cref="Paragraph"/> with the per-paragraph display data <see cref="Views.ParagraphView"/> needs
/// but the parser itself has no reason to know about (docs/superpowers/specs/2026-09-01-books-reader-
/// ergonomics-and-annotations-design.md). <see cref="GlobalOffset"/> is this paragraph's own starting
/// character offset within the chapter's paragraph-concatenated numbering (from
/// <c>BookPaginator.ComputeParagraphOffsets</c>) - what lets the view model translate a
/// <see cref="ParagraphView"/> selection's paragraph-local offsets back into the chapter-global
/// offset scheme <c>BookHighlight</c>/<c>BookBookmark</c> both use.
/// </summary>
public sealed class BookParagraphDisplay
{
    public required BookParagraph Paragraph { get; init; }

    public required int GlobalOffset { get; init; }

    public required IReadOnlyList<ParagraphHighlight> Highlights { get; init; }
}
