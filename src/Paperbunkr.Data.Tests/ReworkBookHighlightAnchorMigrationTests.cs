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
        long bookId;
        using (var context = CreateContext())
        {
            // Straight to PriorMigration from an empty database, not "up to latest, then back down" -
            // going all the way up first would apply every later migration's Up(), including any with
            // a deliberate no-op Down() (this project's own orphan-column rule); rolling back afterward
            // wouldn't remove what that Up() added, so the eventual catch-up-to-latest step further
            // down would collide with it and fail with "duplicate column name". Migrating directly to
            // the target never creates that column in the first place.
            context.GetService<IMigrator>().Migrate(PriorMigration);

            // Written under the pre-migration schema via raw SQL throughout, not context.Books.Add -
            // the physical schema is rolled back to PriorMigration here, which predates unrelated later
            // additions to Books' own columns (docs/superpowers/specs/2026-09-07-books-reader-
            // pagination-and-position-fix-design.md's ReworkBookPositionAnchor), so an EF-model insert
            // against the *current* mapped Book type would throw "no such column" against this
            // older physical table shape.
            context.Database.ExecuteSqlRaw(
                "INSERT INTO Books (Title, Format, FilePath, AddedTime, LastChapterIndex, LastCharacterOffset) VALUES ('Legacy Book', 'Epub', 'C:\\x.epub', {0}, 0, 0)",
                DateTime.UtcNow.ToString("O"));
            bookId = context.Database.SqlQueryRaw<long>("SELECT last_insert_rowid()").ToList().Single();

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
