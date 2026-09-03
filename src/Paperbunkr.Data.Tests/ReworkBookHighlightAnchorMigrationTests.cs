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

            var book = new Book { Title = "Legacy Book", Format = BookFormat.Epub, FilePath = @"C:\x.epub", AddedTime = DateTime.UtcNow };
            context.Books.Add(book);
            context.SaveChanges();

            // Written under the pre-migration schema (StartOffset/EndOffset, no BlockId) via raw SQL,
            // since the current Book Highlight entity no longer has those columns to write through.
            context.Database.ExecuteSqlRaw(
                "INSERT INTO BookHighlights (BookId, ChapterIndex, StartOffset, EndOffset, Color, Excerpt, CreatedTime) VALUES ({0}, 0, 10, 20, 'Yellow', 'legacy excerpt', {1})",
                book.Id, DateTime.UtcNow.ToString("O"));
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
