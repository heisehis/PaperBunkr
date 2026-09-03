using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BookPaginator"/>'s remaining character-offset math (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §5) - still real, load-bearing coverage for
/// <c>BookReaderScreenViewModel</c>'s bookmark-excerpt and in-book search features, per
/// <see cref="BookPaginator"/>'s own updated class doc comment (its page-layout half, <c>FillPage</c>,
/// was removed in the books-reflow-reader-webview-redesign's Step 10 cleanup along with its tests).
/// </summary>
public class BookPaginatorTests
{
    private static BookParagraph P(string text) => new() { Text = text };

    [Fact]
    public void ComputeParagraphOffsets_FirstParagraph_StartsAtZero()
    {
        var paragraphs = new[] { P("Hello"), P("World") };

        int[] offsets = BookPaginator.ComputeParagraphOffsets(paragraphs);

        Assert.Equal(0, offsets[0]);
    }

    [Fact]
    public void ComputeParagraphOffsets_SecondParagraph_AccountsForSeparator()
    {
        var paragraphs = new[] { P("Hello"), P("World") };

        int[] offsets = BookPaginator.ComputeParagraphOffsets(paragraphs);

        // "Hello".Length (5) + ParagraphSeparator.Length (2)
        Assert.Equal(7, offsets[1]);
    }

    [Fact]
    public void FindParagraphIndex_ExactOffset_ReturnsThatParagraph()
    {
        var paragraphs = new[] { P("Hello"), P("World"), P("!") };
        int[] offsets = BookPaginator.ComputeParagraphOffsets(paragraphs);

        Assert.Equal(1, BookPaginator.FindParagraphIndex(paragraphs, offsets[1]));
        Assert.Equal(2, BookPaginator.FindParagraphIndex(paragraphs, offsets[2]));
    }

    [Fact]
    public void FindParagraphIndex_MidParagraphOffset_ReturnsContainingParagraph()
    {
        var paragraphs = new[] { P("Hello"), P("World") };
        int[] offsets = BookPaginator.ComputeParagraphOffsets(paragraphs);

        // A few characters into paragraph 1's text should still resolve to paragraph 1.
        Assert.Equal(1, BookPaginator.FindParagraphIndex(paragraphs, offsets[1] + 2));
    }

    [Fact]
    public void FindParagraphIndex_EmptyChapter_ReturnsZero()
    {
        Assert.Equal(0, BookPaginator.FindParagraphIndex(System.Array.Empty<BookParagraph>(), 0));
    }
}
