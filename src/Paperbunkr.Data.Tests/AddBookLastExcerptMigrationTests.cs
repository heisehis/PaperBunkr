using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <c>AddBookLastExcerpt</c> (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §6.1): existing books keep every row and get a
/// null excerpt (the spread falls back to a highlight, then the synopsis, until the book is opened again). Raw SQL only before and after,
/// pinned to this migration, for the reasons in <see cref="ReworkBookPositionAnchorMigrationTests"/>.
/// </summary>
public class AddBookLastExcerptMigrationTests : IDisposable
{
    private const string PriorMigration = "20261027144920_AddBookReaderLayout";
    private const string ThisMigration = "20261027211329_AddBookLastExcerpt";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_excerpt_migration_test_{Guid.NewGuid():N}.db");

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

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    [Fact]
    public void ExistingBook_KeepsItsRowAndGetsANullExcerpt()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw(
                "INSERT INTO Books (Title, Format, FilePath, AddedTime, LastChapterIndex, Status, SkipMetadataMatch, ChapterCount) " +
                "VALUES ('The Glass Orchard', 'Epub', 'C:/books/orchard.epub', '2026-10-01 00:00:00', 3, 'Reading', 0, 24)");
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);

            Assert.Equal(1, context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Books").ToList().Single());
            Assert.Equal(1, context.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Books WHERE LastExcerpt IS NULL AND LastChapterIndex = 3").ToList().Single());
        }
    }
}
