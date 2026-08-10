using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PdfBookSource"/> (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §4/§9.1) against a real synthetic PDF fixture
/// (<see cref="PdfFixture"/>). The fixture has no outline/bookmarks (PDFium's public C API doesn't
/// expose bookmark *writing*, only reading) - covers the "no chapters found" single-synthetic-
/// chapter path, which is also the path most real-world PDF novels without a tagged outline will
/// hit.
/// </summary>
public class PdfBookSourceTests : IDisposable
{
    private readonly string _pdfPath;

    public PdfBookSourceTests()
    {
        _pdfPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pdf_test_{Guid.NewGuid():N}.pdf");
    }

    public void Dispose()
    {
        if (File.Exists(_pdfPath))
        {
            File.Delete(_pdfPath);
        }
    }

    [Fact]
    public void NoBookmarks_ProducesSingleSyntheticChapter()
    {
        PdfFixture.Create(_pdfPath, "Hello from page one.", "Hello from page two.");

        using var source = new PdfBookSource(_pdfPath);

        var chapter = Assert.Single(source.Chapters);
        Assert.Equal("No chapters found", chapter.Title);
    }

    [Fact]
    public void ExtractsTextFromEveryPage()
    {
        PdfFixture.Create(_pdfPath, "Hello from page one.", "Hello from page two.");

        using var source = new PdfBookSource(_pdfPath);

        string allText = string.Join(" ", source.Chapters[0].Paragraphs.Select(p => p.Text));
        Assert.Contains("Hello from page one.", allText);
        Assert.Contains("Hello from page two.", allText);
    }

    [Fact]
    public void MissingTitleMetadata_FallsBackToFileName()
    {
        PdfFixture.Create(_pdfPath, "Some page text.");

        using var source = new PdfBookSource(_pdfPath);

        Assert.Equal(Path.GetFileNameWithoutExtension(_pdfPath), source.Metadata.Title);
    }

    [Fact]
    public void SeriesName_AlwaysNull_NoPdfEquivalent()
    {
        PdfFixture.Create(_pdfPath, "Some page text.");

        using var source = new PdfBookSource(_pdfPath);

        Assert.Null(source.Metadata.SeriesName);
    }
}
