using System;
using System.Collections.Generic;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Views;

/// <summary>
/// Pure pagination geometry for the Novels reader (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §5), same "pure math, real measurement injected"
/// shape as <see cref="ZoomPanMath"/> - the actual text-height measurement is Avalonia's
/// <c>TextLayout</c> (real UI dependency), injected here as a delegate so the paragraph-fitting
/// algorithm itself stays testable with a fake measurer.
///
/// Pages only ever break at paragraph boundaries - not word/character granularity - matching the
/// design spec's own accepted simplification (§4) that word-wrap within a paragraph is Avalonia's
/// own TextBlock behavior, not something this reader repaginates around.
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

    /// <summary>
    /// Greedily fills a page starting at <paramref name="startParagraphIndex"/>: accumulates
    /// paragraphs until the next one would overflow <paramref name="maxHeight"/>. Always includes
    /// at least one paragraph, even one that alone overflows <paramref name="maxHeight"/> - can't
    /// split below paragraph granularity, so an oversized paragraph just renders taller than the
    /// viewport rather than vanishing.
    /// </summary>
    public static (int Start, int EndExclusive) FillPage(
        IReadOnlyList<BookParagraph> paragraphs,
        int startParagraphIndex,
        double maxHeight,
        double paragraphSpacing,
        Func<BookParagraph, double> measureHeight)
    {
        if (paragraphs.Count == 0 || startParagraphIndex >= paragraphs.Count)
        {
            return (startParagraphIndex, startParagraphIndex);
        }

        double used = 0;
        int end = startParagraphIndex;
        for (int i = startParagraphIndex; i < paragraphs.Count; i++)
        {
            double height = measureHeight(paragraphs[i]) + (i > startParagraphIndex ? paragraphSpacing : 0);
            if (used + height > maxHeight && i > startParagraphIndex)
            {
                break;
            }

            used += height;
            end = i + 1;
        }

        return (startParagraphIndex, end);
    }
}
