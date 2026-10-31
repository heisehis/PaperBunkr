using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <c>AddBookReaderLayout</c> (docs/superpowers/specs/2026-10-27-books-reader-paged-scroll-modes-design.md §8): the existing AppSettings row
/// gets Paged and the 250-300 word range. Raw SQL only before and after, pinned to this migration, for the reasons in
/// <see cref="ReworkBookPositionAnchorMigrationTests"/>.
/// </summary>
public class AddBookReaderLayoutMigrationTests : IDisposable
{
    private const string PriorMigration = "20261027112705_AddBookReaderSpeech";
    private const string ThisMigration = "20261027144920_AddBookReaderLayout";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_layout_migration_test_{Guid.NewGuid():N}.db");

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
    public void ExistingSettingsRow_GetsPagedAndTheDefaultRange()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);

            // ALTER TABLE ADD COLUMN with these defaults is what fills the existing singleton row; the column defaults are the contract.
            string Default(string column) => context.Database
                .SqlQueryRaw<string>($"SELECT dflt_value AS Value FROM pragma_table_info('AppSettings') WHERE name = '{column}'").ToList().Single();

            Assert.Equal("'Paged'", Default("BookReaderLayout"));
            Assert.Equal("250", Default("BookPageWordsMin"));
            Assert.Equal("300", Default("BookPageWordsMax"));
        }
    }
}
