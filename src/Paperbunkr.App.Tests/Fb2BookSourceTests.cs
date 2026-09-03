using System;
using System.IO;
using System.Linq;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Verifies <c>Fb2BookSource</c> (docs/superpowers/specs/2026-09-01-books-format-ingestion-fb2-mobi-
/// design.md) against real hand-built FB2 files from <see cref="Fb2Fixture"/> - metadata/chapter/cover
/// extraction, nested-section flattening, inline bold/italic spans, both bare and .fb2.zip-wrapped,
/// plus a malformed-XML negative case.
/// </summary>
public class Fb2BookSourceTests : IDisposable
{
    private readonly string _fb2Path;
    private readonly string _zipPath;

    public Fb2BookSourceTests()
    {
        _fb2Path = Path.Combine(Path.GetTempPath(), $"paperbunkr_fb2_test_{Guid.NewGuid():N}.fb2");
        _zipPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_fb2_test_{Guid.NewGuid():N}.fb2.zip");
    }

    public void Dispose()
    {
        if (File.Exists(_fb2Path)) File.Delete(_fb2Path);
        if (File.Exists(_zipPath)) File.Delete(_zipPath);
    }

    [Fact]
    public void Metadata_ExtractsTitleAuthorSeriesAndCover()
    {
        Fb2Fixture.Create(_fb2Path, title: "The Long Way Home", author: "Ada Author", seriesName: "The Chronicles");

        using var source = new Fb2BookSource(_fb2Path);

        Assert.Equal("The Long Way Home", source.Metadata.Title);
        Assert.Equal("Ada Author", source.Metadata.Author);
        Assert.Equal("The Chronicles", source.Metadata.SeriesName);
        Assert.NotNull(source.Metadata.CoverImageBytes);
        Assert.True(source.Metadata.CoverImageBytes!.Length > 0);
    }

    [Fact]
    public void Metadata_SeriesIsNull_WhenNoSequenceElement()
    {
        Fb2Fixture.Create(_fb2Path, seriesName: null);

        using var source = new Fb2BookSource(_fb2Path);

        Assert.Null(source.Metadata.SeriesName);
    }

    [Fact]
    public void Chapters_OneChapterPerTopLevelSection_WithTitlesFromTitleElement()
    {
        Fb2Fixture.Create(_fb2Path);

        using var source = new Fb2BookSource(_fb2Path);

        Assert.Equal(2, source.Chapters.Count);
        Assert.Equal("The Beginning", source.Chapters[0].Title);
        Assert.Equal("The End", source.Chapters[1].Title);
    }

    [Fact]
    public void NestedSection_FlattensIntoParentChapter_NotItsOwnChapter()
    {
        Fb2Fixture.Create(_fb2Path);

        using var source = new Fb2BookSource(_fb2Path);

        // Still 2 chapters overall - the nested <section> under "The Beginning" didn't add a third.
        Assert.Equal(2, source.Chapters.Count);

        var firstChapterText = string.Join(" ", source.Chapters[0].Paragraphs.Select(p => p.Text));
        Assert.Contains("A Sub Heading", firstChapterText);
        Assert.Contains("Nested content that should flatten into the parent chapter.", firstChapterText);
    }

    [Fact]
    public void InlineSpans_MapStrongToBold_AndEmphasisToItalic()
    {
        Fb2Fixture.Create(_fb2Path);

        using var source = new Fb2BookSource(_fb2Path);

        var firstParagraph = source.Chapters[0].Paragraphs[0];
        Assert.Equal("It was a dark and stormy night.", firstParagraph.Text);
        var boldSpan = Assert.Single(firstParagraph.Spans);
        Assert.True(boldSpan.Bold);
        Assert.False(boldSpan.Italic);
        Assert.Equal("dark", firstParagraph.Text.Substring(boldSpan.Start, boldSpan.Length));

        var secondParagraph = source.Chapters[0].Paragraphs[1];
        var italicSpan = Assert.Single(secondParagraph.Spans);
        Assert.True(italicSpan.Italic);
        Assert.False(italicSpan.Bold);
        Assert.Equal("slowly", secondParagraph.Text.Substring(italicSpan.Start, italicSpan.Length));
    }

    [Fact]
    public void ZipWrapped_ParsesIdenticallyToBareFile()
    {
        Fb2Fixture.Create(_zipPath, title: "Zipped Novel", zipWrapped: true);

        using var source = new Fb2BookSource(_zipPath);

        Assert.Equal("Zipped Novel", source.Metadata.Title);
        Assert.Equal(2, source.Chapters.Count);
        Assert.Equal("The Beginning", source.Chapters[0].Title);
    }

    [Fact]
    public void MalformedXml_ThrowsCleanly_NotSilentEmptyResult()
    {
        Fb2Fixture.Create(_fb2Path, malformed: true);

        Assert.ThrowsAny<Exception>(() => new Fb2BookSource(_fb2Path));
    }

    [Fact]
    public void Html_ContainsRealMarkup_WithBoldItalicAndFlattenedNestedSection()
    {
        Fb2Fixture.Create(_fb2Path);

        using var source = new Fb2BookSource(_fb2Path);

        string html = source.Chapters[0].Html ?? string.Empty;
        Assert.Contains("<p>It was a <strong>dark</strong> and stormy night.</p>", html);
        Assert.Contains("<p>She walked <em>slowly</em> into the fog.</p>", html);
        // The nested sub-section's title becomes a heading, flattened into the same chapter's HTML
        // rather than a separate chapter - same rule Paragraphs already follows.
        Assert.Contains("A Sub Heading", html);
        Assert.Contains("Nested content that should flatten into the parent chapter.", html);
    }

    [Fact]
    public void Html_ResolvesInlineImage_AgainstBinaryBlock_UsingItsRealContentType()
    {
        byte[] pngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        string base64 = Convert.ToBase64String(pngBytes);
        File.WriteAllText(_fb2Path, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0" xmlns:l="http://www.w3.org/1999/xlink">
              <body>
                <section>
                  <p>Before the illustration.</p>
                  <image l:href="#illustration.png"/>
                  <p>After the illustration.</p>
                </section>
              </body>
              <binary id="illustration.png" content-type="image/png">{base64}</binary>
            </FictionBook>
            """);

        using var source = new Fb2BookSource(_fb2Path);

        string html = Assert.Single(source.Chapters).Html ?? string.Empty;
        Assert.Contains($"<img src=\"data:image/png;base64,{base64}\" />", html);
    }

    [Fact]
    public void Title_FallsBackToFileName_WhenBookTitleMissing()
    {
        File.WriteAllText(_fb2Path, """
            <?xml version="1.0" encoding="UTF-8"?>
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0">
              <body>
                <p>No title-info at all, just body text.</p>
              </body>
            </FictionBook>
            """);

        using var source = new Fb2BookSource(_fb2Path);

        Assert.Equal(Path.GetFileNameWithoutExtension(_fb2Path), source.Metadata.Title);
        var chapter = Assert.Single(source.Chapters);
        Assert.Equal("No title-info at all, just body text.", chapter.Paragraphs[0].Text);
    }
}
