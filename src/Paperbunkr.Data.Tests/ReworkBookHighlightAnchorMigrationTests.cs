using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>ReworkBookHighlightAnchor</c> migration (docs/superpowers/specs/2026-09-02-books-
/// reflow-reader-webview-redesign-design.md) - unlike a normal additive migration, this one is
/// expected to *discard* existing <c>BookHighlight</c> rows (the old StartOffset/EndOffset anchor has
/// no meaningful mapping onto the new BlockId/StartOffset/Length one), per the design's explicit
/// reset-not-migrate decision. Asserts the deletion actually happens and the new columns work for
/// data written after the migration.
/// </summary>
public class ReworkBookHighlightAnchorMigrationTests : IDisposable
{
    private const string PriorMigration = "20260902142325_AddLastContentTypeSweepUtc";
    private readonly string _dbPath;

    public ReworkBookHighlightAnchorMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_highlightanchor_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_DeletesExistingHighlights_AndNewColumnsWorkAfterward()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetService<IMigrator>().Migrate(PriorMigration);

            // Written via raw SQL rather than context.Books.Add(...), since the current Book entity
            // is mapped to columns (e.g. CharacterCount, added later by AddReadingEventLog) that
            // don't exist in the schema at this rolled-back point - an EF-generated INSERT would
            // reference them and fail with "no such column".
            context.Database.ExecuteSqlRaw(
                "INSERT INTO Books (Title, FilePath, Format, AddedTime, LastChapterIndex, LastCharacterOffset) VALUES ({0}, {1}, {2}, {3}, 0, 0)",
                "Legacy Book", @"C:\x.epub", nameof(BookFormat.Epub), DateTime.UtcNow.ToString("O"));

            // Looked up by Title rather than last_insert_rowid(), which is scoped to whichever
            // physical connection executes it - not guaranteed to be the same one that ran the
            // INSERT above once EF's connection pooling is involved.
            var bookId = context.Database
                .SqlQueryRaw<int>("SELECT Id AS Value FROM Books WHERE Title = {0}", "Legacy Book")
                .Single();

            // Written under the pre-migration schema (StartOffset/EndOffset, no BlockId) via raw SQL,
            // since the current Book Highlight entity no longer has those columns to write through.
            context.Database.ExecuteSqlRaw(
                "INSERT INTO BookHighlights (BookId, ChapterIndex, StartOffset, EndOffset, Color, Excerpt, CreatedTime) VALUES ({0}, 0, 10, 20, 'Yellow', 'legacy excerpt', {1})",
                bookId, DateTime.UtcNow.ToString("O"));
        }

        using (var context = CreateContext())
        {
            context.Database.Migrate();

            Assert.Empty(context.BookHighlights);

            var cols = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('BookHighlights') WHERE name IN ('BlockId', 'Length', 'EndOffset');")
                .ToList();
            Assert.Contains("BlockId", cols);
            Assert.Contains("Length", cols);
            Assert.DoesNotContain("EndOffset", cols);
        }

        using (var context = CreateContext())
        {
            var book = context.Books.Single();
            context.BookHighlights.Add(new BookHighlight
            {
                BookId = book.Id, ChapterIndex = 0, BlockId = "pb-p3", StartOffset = 5, Length = 12,
                Color = BookHighlightColor.Blue, Excerpt = "fresh excerpt", CreatedTime = DateTime.UtcNow,
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var highlight = context.BookHighlights.Single();
            Assert.Equal("pb-p3", highlight.BlockId);
            Assert.Equal(5, highlight.StartOffset);
            Assert.Equal(12, highlight.Length);
        }
    }
}
