using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="AnnotationExportService"/> (docs/superpowers/specs/2026-09-01-books-reader-
/// ergonomics-and-annotations-design.md §"Export") against a fixture book with one bookmark, one
/// highlight, and one annotation image. Uses a non-existent <see cref="Book.FilePath"/> - this project
/// has no EPUB-fixture-authoring infrastructure the way <c>Paperbunkr.App.Tests</c> does, so chapter
/// titles exercise <see cref="AnnotationExportService"/>'s own documented fallback path ("Chapter N")
/// rather than a real parsed title; the export structure/formatting itself is what these tests verify.
/// </summary>
public class AnnotationExportServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _outputDir;
    private readonly int _bookId;
    private readonly string _capturedImagePath;

    public AnnotationExportServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_export_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _outputDir = Path.Combine(Path.GetTempPath(), $"paperbunkr_export_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDir);

        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var book = new Book
        {
            Title = "Export Test Book", Author = "A. Author", Format = BookFormat.Epub,
            FilePath = @"C:\does-not-exist.epub", AddedTime = DateTime.UtcNow,
        };
        context.Books.Add(book);
        context.SaveChanges();
        _bookId = book.Id;

        _capturedImagePath = Path.Combine(_outputDir, "source_capture.png");
        File.WriteAllBytes(_capturedImagePath, [137, 80, 78, 71]); // minimal placeholder bytes - content irrelevant, only presence/copy is tested

        context.BookBookmarks.Add(new BookBookmark
        {
            BookId = _bookId, ChapterIndex = 0, CharacterOffset = 10, Excerpt = "A bookmarked line.", CreatedTime = DateTime.UtcNow,
        });
        context.BookHighlights.Add(new BookHighlight
        {
            BookId = _bookId, ChapterIndex = 0, StartOffset = 0, EndOffset = 5, Color = BookHighlightColor.Green,
            Note = "worth remembering", Excerpt = "Hello", CreatedTime = DateTime.UtcNow,
        });
        context.BookAnnotationImages.Add(new BookAnnotationImage
        {
            BookId = _bookId, PageIndex = 2, RectX = 0.1, RectY = 0.1, RectWidth = 0.2, RectHeight = 0.2,
            ImagePath = _capturedImagePath, Note = "a diagram", CreatedTime = DateTime.UtcNow,
        });
        context.SaveChanges();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext CreateContext() => new(_dbOptions);

    [Fact]
    public void ExportMarkdown_ProducesChapterHeadingAndBlockquotedContent_AndCopiesTheImage()
    {
        string outputPath = Path.Combine(_outputDir, "export.md");
        using (var context = CreateContext())
        {
            AnnotationExportService.ExportMarkdown(context, _bookId, outputPath);
        }

        string content = File.ReadAllText(outputPath);
        Assert.Contains("# Export Test Book", content);
        Assert.Contains("## Chapter 1", content);
        Assert.Contains("> 🔖 A bookmarked line.", content);
        Assert.Contains("[Green]", content);
        Assert.Contains("Hello", content);
        Assert.Contains("worth remembering", content);
        Assert.Contains("## Captured Regions", content);
        Assert.Contains("Page 3", content);
        Assert.Contains("export_images/", content);

        string copiedImage = Path.Combine(_outputDir, "export_images", Directory.GetFiles(Path.Combine(_outputDir, "export_images")).Single());
        Assert.True(File.Exists(copiedImage));
    }

    [Fact]
    public void ExportCsv_ProducesOneFlatRowPerAnnotation_NoImages()
    {
        string outputPath = Path.Combine(_outputDir, "export.csv");
        using (var context = CreateContext())
        {
            AnnotationExportService.ExportCsv(context, _bookId, outputPath);
        }

        var lines = File.ReadAllLines(outputPath);
        Assert.Equal("Type,ChapterOrPage,Excerpt,Note,Color,CreatedTime", lines[0]);
        Assert.Equal(4, lines.Length); // header + bookmark + highlight + capture
        Assert.Contains(lines, l => l.StartsWith("Bookmark,1,"));
        Assert.Contains(lines, l => l.StartsWith("Highlight,1,Hello,worth remembering,Green,"));
        Assert.Contains(lines, l => l.StartsWith("Capture,3,,a diagram,,"));
        Assert.False(Directory.Exists(Path.Combine(_outputDir, "export_images")));
    }

    [Fact]
    public void ExportJson_IncludesFullStructureAndImagePaths()
    {
        string outputPath = Path.Combine(_outputDir, "export.json");
        using (var context = CreateContext())
        {
            AnnotationExportService.ExportJson(context, _bookId, outputPath);
        }

        string json = File.ReadAllText(outputPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Export Test Book", root.GetProperty("Title").GetString());
        Assert.Single(root.GetProperty("Bookmarks").EnumerateArray());
        Assert.Single(root.GetProperty("Highlights").EnumerateArray());

        var image = root.GetProperty("AnnotationImages").EnumerateArray().Single();
        Assert.Equal(2, image.GetProperty("PageIndex").GetInt32());
        Assert.Contains("export_images/", image.GetProperty("ImagePath").GetString());
    }
}
