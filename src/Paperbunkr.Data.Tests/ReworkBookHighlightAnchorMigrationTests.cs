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
            // Straight to PriorMigration from an empty database, not "up to latest, then back down" -
            // going all the way up first would apply every later migration's Up(), including any with
            // a deliberate no-op Down() (this project's own orphan-column rule); rolling back afterward
            // wouldn't remove what that Up() added, so the eventual catch-up-to-latest step further
            // down would collide with it and fail with "duplicate column name". Migrating directly to
            // the target never creates that column in the first place.
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
            // Targets this migration specifically, not "whatever's latest" - context.Database.Migrate()
            // would re-run every later migration too, including any with a deliberate no-op Down()
            // (this project's own orphan-column rule). Since Down() doesn't remove such a column, a
            // second Up() pass over it (which the up-down-up shape above requires) fails with
            // "duplicate column name" - this is what was actually behind the previously-tracked
            // "duplicate column name: CharacterCount" failure, not a genuine EF/SQLite rebuild bug.
            context.GetService<IMigrator>().Migrate("20260902162546_ReworkBookHighlightAnchor");

            Assert.Empty(context.BookHighlights);

            var cols = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('BookHighlights') WHERE name IN ('BlockId', 'Length', 'EndOffset');")
                .ToList();
            Assert.Contains("BlockId", cols);
            Assert.Contains("Length", cols);
            Assert.DoesNotContain("EndOffset", cols);
        }

        // Catches the schema up to whatever's actually newest (deliberately unpinned, unlike the
        // block above) - everything from here on uses plain typed EF operations against the
        // *current* model, which only work once the physical schema has every later migration's
        // columns too (e.g. Book.LastBlockId from ReworkBookPositionAnchor).
        using (var context = CreateContext())
        {
            context.Database.Migrate();
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
