using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>ReworkBookPositionAnchor</c> migration (docs/superpowers/specs/2026-09-07-books-
/// reader-pagination-and-position-fix-design.md) - like <c>ReworkBookHighlightAnchor</c> before it,
/// this one is expected to *discard* existing <c>BookBookmark</c> rows and reset
/// <c>Book.LastChapterIndex</c> (the old CharacterOffset-based anchor has no meaningful mapping onto
/// the new BlockId one), per the design's explicit reset decision. Asserts the deletion/reset actually
/// happens and the new columns work for data written after the migration.
///
/// Everything before the forward <c>context.Database.Migrate()</c> call goes through raw SQL, never
/// the EF model (<c>context.Books.Add</c>/<c>context.Books.Single</c> etc.) - the physical schema is
/// still rolled back to <see cref="PriorMigration"/> at that point, which predates this migration's own
/// new <c>Books</c>/<c>BookBookmarks</c> columns, so any EF read/write against those tables would throw
/// "no such column" against the *current* mapped model.
/// </summary>
public class ReworkBookPositionAnchorMigrationTests : IDisposable
{
    private const string PriorMigration = "20260906175855_AddScanMissingFileHandling";
    private readonly string _dbPath;

    public ReworkBookPositionAnchorMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_positionanchor_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_DeletesBookmarksAndResetsLastChapterIndex_AndNewColumnsWorkAfterward()
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

            context.Database.ExecuteSqlRaw(
                "INSERT INTO Books (Title, Format, FilePath, AddedTime, LastChapterIndex, LastCharacterOffset) VALUES ('Legacy Book', 'Epub', 'C:\\x.epub', {0}, 3, 250)",
                DateTime.UtcNow.ToString("O"));
            bookId = context.Database.SqlQueryRaw<long>("SELECT last_insert_rowid()").ToList().Single();

            context.Database.ExecuteSqlRaw(
                "INSERT INTO BookBookmarks (BookId, ChapterIndex, CharacterOffset, Excerpt, CreatedTime) VALUES ({0}, 2, 80, 'legacy excerpt', {1})",
                bookId, DateTime.UtcNow.ToString("O"));
        }

        using (var context = CreateContext())
        {
            // Targets this migration specifically, not "whatever's latest" - context.Database.Migrate()
            // would re-run every later migration too, including any with a deliberate no-op Down()
            // (this project's own orphan-column rule). Since Down() doesn't remove such a column, a
            // second Up() pass over it (which the up-down-up shape above requires) fails with
            // "duplicate column name" - the exact failure mode this pin avoids regardless of what
            // gets added after this migration in the future.
            context.GetService<IMigrator>().Migrate("20260907060613_ReworkBookPositionAnchor");

            Assert.Empty(context.BookBookmarks);

            var book = context.Books.Single(b => b.Id == bookId);
            Assert.Equal(0, book.LastChapterIndex);
            Assert.Null(book.LastBlockId);
            Assert.Null(book.LastProgressionFraction);

            var bookCols = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Books') WHERE name IN ('LastBlockId', 'LastProgressionFraction', 'LastCharacterOffset');")
                .ToList();
            Assert.Contains("LastBlockId", bookCols);
            Assert.Contains("LastProgressionFraction", bookCols);
            Assert.DoesNotContain("LastCharacterOffset", bookCols);

            var bookmarkCols = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('BookBookmarks') WHERE name IN ('BlockId', 'ProgressionFraction', 'CharacterOffset');")
                .ToList();
            Assert.Contains("BlockId", bookmarkCols);
            Assert.Contains("ProgressionFraction", bookmarkCols);
            Assert.DoesNotContain("CharacterOffset", bookmarkCols);
        }

        // Catches the schema up to whatever's actually newest (deliberately unpinned, unlike the
        // block above) - everything from here on uses plain typed EF operations against the
        // *current* model, which only work once the physical schema has every later migration's
        // columns too. Currently a no-op in practice (this migration is still the last one to touch
        // Books/BookBookmarks), but keeps this test from breaking the same way
        // ReworkBookHighlightAnchorMigrationTests did the moment that stops being true.
        using (var context = CreateContext())
        {
            context.Database.Migrate();
        }

        using (var context = CreateContext())
        {
            var book = context.Books.Single(b => b.Id == bookId);
            book.LastBlockId = "pb-p7";
            book.LastProgressionFraction = 0.4;
            context.BookBookmarks.Add(new BookBookmark
            {
                BookId = (int)bookId, ChapterIndex = 1, BlockId = "pb-p3", ProgressionFraction = 0.1,
                Excerpt = "fresh excerpt", CreatedTime = DateTime.UtcNow,
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var book = context.Books.Single(b => b.Id == bookId);
            Assert.Equal("pb-p7", book.LastBlockId);
            Assert.Equal(0.4, book.LastProgressionFraction);

            var bookmark = context.BookBookmarks.Single();
            Assert.Equal("pb-p3", bookmark.BlockId);
            Assert.Equal(0.1, bookmark.ProgressionFraction);
        }
    }
}
