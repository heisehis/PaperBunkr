using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="EpubBookSource"/> (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §4) against a real synthetic EPUB fixture
/// (<see cref="EpubFixture"/>), same "generate via the real code path" precedent as
/// <see cref="LibraryFolderScannerTests"/>.
/// </summary>
public class EpubBookSourceTests : IDisposable
{
    private readonly string _epubPath;

    public EpubBookSourceTests()
    {
        _epubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_epub_test_{Guid.NewGuid():N}.epub");
    }

    public void Dispose()
    {
        if (File.Exists(_epubPath))
        {
            File.Delete(_epubPath);
        }
    }

    [Fact]
    public void Metadata_ReadsTitleAndAuthor()
    {
        EpubFixture.Create(_epubPath, title: "The Long Road", author: "Ada Author");

        using var source = new EpubBookSource(_epubPath);

        Assert.Equal("The Long Road", source.Metadata.Title);
        Assert.Equal("Ada Author", source.Metadata.Author);
    }

    [Fact]
    public void Metadata_CalibreSeriesMeta_ReadsSeriesName()
    {
        EpubFixture.Create(_epubPath, seriesName: "The Long Road Trilogy");

        using var source = new EpubBookSource(_epubPath);

        Assert.Equal("The Long Road Trilogy", source.Metadata.SeriesName);
    }

    [Fact]
    public void Metadata_NoSeriesMeta_SeriesNameIsNull()
    {
        EpubFixture.Create(_epubPath);

        using var source = new EpubBookSource(_epubPath);

        Assert.Null(source.Metadata.SeriesName);
    }

    [Fact]
    public void Chapters_OneChapterPerSpineItem_InReadingOrder()
    {
        EpubFixture.Create(_epubPath);

        using var source = new EpubBookSource(_epubPath);

        Assert.Equal(2, source.Chapters.Count);
        Assert.Equal("The Beginning", source.Chapters[0].Title);
        Assert.Equal("The End", source.Chapters[1].Title);
    }

    [Fact]
    public void Chapters_ParagraphsExtracted_TagsAndEntitiesStripped()
    {
        EpubFixture.Create(_epubPath);

        using var source = new EpubBookSource(_epubPath);

        var chapter1 = source.Chapters[0];
        // <h1> + 2 <p> = 3 paragraphs; heading text included since h1 is a paragraph-break tag too.
        Assert.Equal(3, chapter1.Paragraphs.Count);
        Assert.Equal("The Beginning", chapter1.Paragraphs[0].Text);
        Assert.Equal("It was a dark and stormy night.", chapter1.Paragraphs[1].Text);
        Assert.Equal("She walked slowly into the fog.", chapter1.Paragraphs[2].Text);
    }

    [Fact]
    public void Chapters_BoldAndItalicSpans_CapturedWithCorrectOffsets()
    {
        EpubFixture.Create(_epubPath);

        using var source = new EpubBookSource(_epubPath);

        var darkParagraph = source.Chapters[0].Paragraphs[1];
        var boldSpan = Assert.Single(darkParagraph.Spans, s => s.Bold);
        Assert.Equal("dark", darkParagraph.Text.Substring(boldSpan.Start, boldSpan.Length));

        var slowlyParagraph = source.Chapters[0].Paragraphs[2];
        var italicSpan = Assert.Single(slowlyParagraph.Spans, s => s.Italic);
        Assert.Equal("slowly", slowlyParagraph.Text.Substring(italicSpan.Start, italicSpan.Length));
    }

    // --- TOC grouping (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md) -
    // PartTitle comes from EPUB nav's own real hierarchy (EpubNavigationItem.NestedItems), not
    // invented parsing - EpubFixture(withParts: true) builds chap1 as a standalone top-level nav
    // item and chap2/chap3 nested under a "Part One" heading, matching real EPUB3 nav structure. ---

    [Fact]
    public void Chapters_WithoutNestedNavItems_HaveNullPartTitle()
    {
        EpubFixture.Create(_epubPath); // flat 2-chapter nav, no nesting

        using var source = new EpubBookSource(_epubPath);

        Assert.All(source.Chapters, c => Assert.Null(c.PartTitle));
    }

    [Fact]
    public void Chapters_NestedUnderAPartHeading_GetThatPartsTitle()
    {
        EpubFixture.Create(_epubPath, withParts: true);

        using var source = new EpubBookSource(_epubPath);

        Assert.Equal(3, source.Chapters.Count);
        Assert.Null(source.Chapters[0].PartTitle); // chap1: top-level, no nesting
        Assert.Equal("Part One", source.Chapters[1].PartTitle); // chap2: nested under Part One
        Assert.Equal("Part One", source.Chapters[2].PartTitle); // chap3: nested under Part One
    }
}
