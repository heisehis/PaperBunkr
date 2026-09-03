using System;
using System.Collections.Generic;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Views;

/// <summary>
/// Character-offset math for the Novels reader's <see cref="BookParagraph"/> collections
/// (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §5). Originally paired with
/// a paragraph-fitting page-layout algorithm too (<c>FillPage</c>), removed in the books-reflow-
/// reader-webview-redesign's Step 10 cleanup (docs/superpowers/specs/2026-09-02-books-reflow-reader-
/// webview-redesign-design.md) once the reading pane itself moved to a real HTML-rendering WebView
/// and stopped doing its own text layout entirely. What's left here is still load-bearing, not
/// vestigial: <see cref="BookReaderScreenViewModel.ToggleBookmark"/> uses
/// <see cref="FindParagraphIndex"/> to build a bookmark's excerpt text, and
/// <see cref="BookReaderScreenViewModel.RunSearch"/> uses <see cref="ComputeParagraphOffsets"/> to
/// report a search result's character offset - both real, currently-shipping features, independent
/// of how the reading pane itself renders.
/// </summary>
public static class BookPaginator
{
    /// <summary>Separator BookReaderScreenViewModel/BookPosition math assumes between paragraphs when computing character offsets.</summary>
    public const string ParagraphSeparator = "\n\n";

    /// <summary>Character offset (within the chapter's paragraphs joined by <see cref="ParagraphSeparator"/>) that each paragraph starts at.</summary>
    public static int[] ComputeParagraphOffsets(IReadOnlyList<BookParagraph> paragraphs)
    {
        var offsets = new int[paragraphs.Count];
        int running = 0;
        for (int i = 0; i < paragraphs.Count; i++)
        {
            offsets[i] = running;
            running += paragraphs[i].Text.Length + ParagraphSeparator.Length;
        }

        return offsets;
    }

    /// <summary>Maps a character offset back to the paragraph it falls within (or the closest preceding one).</summary>
    public static int FindParagraphIndex(IReadOnlyList<BookParagraph> paragraphs, int characterOffset)
    {
        if (paragraphs.Count == 0)
        {
            return 0;
        }

        int[] offsets = ComputeParagraphOffsets(paragraphs);
        int index = Array.BinarySearch(offsets, characterOffset);
        if (index >= 0)
        {
            return index;
        }

        int insertionPoint = ~index;
        return Math.Clamp(insertionPoint - 1, 0, paragraphs.Count - 1);
    }
}
