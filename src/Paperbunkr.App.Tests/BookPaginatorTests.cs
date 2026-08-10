using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BookPaginator"/>'s pure paragraph-fitting geometry (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §5), same "pure math, fake measurer injected"
/// shape as <see cref="ZoomPanMathTests"/> - real Avalonia text measurement is
/// <see cref="Paperbunkr.App.ViewModels.BookReaderScreenViewModel"/>'s own concern, not this.
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

    [Fact]
    public void FillPage_AllParagraphsFit_ReturnsWholeChapter()
    {
        var paragraphs = new[] { P("a"), P("b"), P("c") };

        var (start, end) = BookPaginator.FillPage(paragraphs, 0, maxHeight: 1000, paragraphSpacing: 5, measureHeight: _ => 50);

        Assert.Equal(0, start);
        Assert.Equal(3, end);
    }

    [Fact]
    public void FillPage_OnlySomeParagraphsFit_StopsBeforeOverflow()
    {
        var paragraphs = new[] { P("a"), P("b"), P("c"), P("d") };
        // Each paragraph is 100 tall + 10 spacing between = 110 per paragraph after the first.
        // maxHeight 250 fits: 100 (p0) + 110 (p1) = 210, then +110 (p2) = 320 > 250 -> stop at p2.

        var (start, end) = BookPaginator.FillPage(paragraphs, 0, maxHeight: 250, paragraphSpacing: 10, measureHeight: _ => 100);

        Assert.Equal(0, start);
        Assert.Equal(2, end);
    }

    [Fact]
    public void FillPage_SingleOversizedParagraph_StillIncludedAlone()
    {
        var paragraphs = new[] { P("a really long paragraph"), P("b") };

        var (start, end) = BookPaginator.FillPage(paragraphs, 0, maxHeight: 50, paragraphSpacing: 5, measureHeight: _ => 500);

        // Can't split below paragraph granularity - the first paragraph alone overflows maxHeight
        // but must still be included, and the second paragraph must not be (page can't be empty
        // but also shouldn't silently absorb more than the budget once already over).
        Assert.Equal(0, start);
        Assert.Equal(1, end);
    }

    [Fact]
    public void FillPage_ContinuingFromMidChapter_StartsAtGivenIndex()
    {
        var paragraphs = new[] { P("a"), P("b"), P("c"), P("d") };

        var (start, end) = BookPaginator.FillPage(paragraphs, startParagraphIndex: 2, maxHeight: 1000, paragraphSpacing: 5, measureHeight: _ => 50);

        Assert.Equal(2, start);
        Assert.Equal(4, end);
    }

    [Fact]
    public void FillPage_EmptyChapter_ReturnsEmptyRange()
    {
        var (start, end) = BookPaginator.FillPage(System.Array.Empty<BookParagraph>(), 0, maxHeight: 1000, paragraphSpacing: 5, measureHeight: _ => 50);

        Assert.Equal(start, end);
    }
}
