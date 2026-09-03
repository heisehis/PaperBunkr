using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the hand-edited <c>LibraryPosterGridConsolidation</c> migration's raw-SQL remap
/// (docs/superpowers/specs/2026-08-27-library-browsing-4a-poster-grid-design.md §3) against a real
/// SQLite database carrying a pre-migration <c>AppSettings</c> row with a legacy
/// <c>LibraryViewMode</c> string - same shape as <see cref="AddIssueTagsMigrationTests"/>.
/// </summary>
public class LibraryPosterGridMigrationTests : IDisposable
{
    private const string PriorMigration = "20260827035758_AddRenderingBackendSettings";
    private readonly string _dbPath;

    public LibraryPosterGridMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_postergrid_migration_test_{Guid.NewGuid():N}.db");
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

    private (string ViewMode, long ShowTitles) MigrateWithSeededViewMode(string legacyValue)
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            // The singleton settings row isn't seeded by migrations - insert one with the legacy
            // view mode; every other NOT NULL column has a DB-level default from earlier migrations.
            context.Database.ExecuteSqlRaw(
                $"INSERT INTO AppSettings (Id, LibraryViewMode) VALUES (1, '{legacyValue}');");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var viewMode = context.Database
                .SqlQueryRaw<string>("SELECT LibraryViewMode AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            var showTitles = context.Database
                .SqlQueryRaw<long>("SELECT LibraryShowTileTitles AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            return (viewMode, showTitles);
        }
    }

    [Fact]
    public void Migration_CoverOnlyGrid_BecomesPosterGrid_WithTitlesOff()
    {
        var (viewMode, showTitles) = MigrateWithSeededViewMode("CoverOnlyGrid");

        Assert.Equal("PosterGrid", viewMode);
        Assert.Equal(0, showTitles);
    }

    [Theory]
    [InlineData("CompactGrid")]
    [InlineData("ComfortableGrid")]
    public void Migration_LegacyGrid_BecomesPosterGrid_WithTitlesStillOn(string legacy)
    {
        var (viewMode, showTitles) = MigrateWithSeededViewMode(legacy);

        Assert.Equal("PosterGrid", viewMode);
        Assert.Equal(1, showTitles);
    }

    [Fact]
    public void Migration_NonGridMode_IsUnchanged()
    {
        // "List" survives every later migration untouched (unlike "IssueList", which a still-later
        // migration remaps to "Details" - see RemoveComicListViewModeMigrationTests).
        var (viewMode, showTitles) = MigrateWithSeededViewMode("List");

        Assert.Equal("List", viewMode);
        Assert.Equal(1, showTitles);
    }
}
