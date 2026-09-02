using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddBookReaderErgonomicsAndAnnotations</c> migration (docs/superpowers/specs/
/// 2026-09-01-books-reader-ergonomics-and-annotations-design.md) - 8 nullable override columns on
/// Books, 9 columns with defaults on AppSettings, and two new tables (BookHighlights,
/// BookAnnotationImages). Same shape as <see cref="AddBooksBrowseStateMigrationTests"/>.
/// </summary>
public class AddBookReaderErgonomicsAndAnnotationsMigrationTests : IDisposable
{
    private const string PriorMigration = "20260901054450_AddCheckForUpdatesOnStartup";
    private readonly string _dbPath;

    public AddBookReaderErgonomicsAndAnnotationsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookreadererg_migration_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_AddsColumnsAndTablesWithDefaults_PreservingExistingRows_AndIsReversible()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetOrCreateAppSettings();
            context.Books.Add(new Book
            {
                Title = "Legacy Book", Format = BookFormat.Epub, FilePath = @"C:\x.epub",
                AddedTime = new DateTime(2024, 1, 1),
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.Equal(17.0, settings.BookReaderFontSize);
            Assert.Equal(BookFontFamilyOption.Serif, settings.BookReaderFontFamily);
            Assert.Equal(BookLineSpacingOption.Normal, settings.BookReaderLineSpacing);
            Assert.Equal(0.0, settings.BookReaderCharacterSpacing);
            Assert.Equal(0.0, settings.BookReaderWordSpacing);
            Assert.Equal(10.0, settings.BookReaderParagraphSpacing);
            Assert.Equal(40.0, settings.BookReaderPageMargin);
            Assert.Equal(BookTheme.MatchAppSkin, settings.BookReaderTheme);
            Assert.True(settings.BookReaderAutoHideChrome);

            var book = context.Books.Single();
            Assert.Null(book.FontSizeOverride);
            Assert.Null(book.FontFamilyOverride);
            Assert.Null(book.LineSpacingOverride);
            Assert.Null(book.CharacterSpacingOverride);
            Assert.Null(book.WordSpacingOverride);
            Assert.Null(book.ParagraphSpacingOverride);
            Assert.Null(book.PageMarginOverride);
            Assert.Null(book.ThemeOverride);

            context.BookHighlights.Add(new BookHighlight
            {
                BookId = book.Id, ChapterIndex = 0, StartOffset = 0, EndOffset = 10,
                Color = BookHighlightColor.Green, Excerpt = "hi", CreatedTime = DateTime.UtcNow,
            });
            context.BookAnnotationImages.Add(new BookAnnotationImage
            {
                BookId = book.Id, PageIndex = 0, RectX = 0.1, RectY = 0.1, RectWidth = 0.2, RectHeight = 0.2,
                ImagePath = @"C:\annotations\x.png", CreatedTime = DateTime.UtcNow,
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var book = context.Books.Include(b => b.Highlights).Include(b => b.AnnotationImages).Single();
            Assert.Single(book.Highlights);
            Assert.Equal(BookHighlightColor.Green, book.Highlights[0].Color);
            Assert.Single(book.AnnotationImages);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var bookCols = context.Database
                .SqlQueryRaw<string>("""
                    SELECT name FROM pragma_table_info('Books')
                    WHERE name IN ('FontSizeOverride', 'FontFamilyOverride', 'LineSpacingOverride',
                                    'CharacterSpacingOverride', 'WordSpacingOverride',
                                    'ParagraphSpacingOverride', 'PageMarginOverride', 'ThemeOverride');
                    """)
                .ToList();
            Assert.Empty(bookCols);

            var tables = context.Database
                .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name IN ('BookHighlights', 'BookAnnotationImages');")
                .ToList();
            Assert.Empty(tables);

            var bookCount = context.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM Books")
                .Single();
            Assert.Equal(1, bookCount);
        }
    }
}
