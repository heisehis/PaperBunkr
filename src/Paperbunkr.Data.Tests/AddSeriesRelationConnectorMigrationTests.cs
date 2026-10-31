using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <c>AddSeriesRelationConnector</c> (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §4): an existing settings row
/// gets the connector switched on, an existing series is "never checked with Metron", and existing associations keep their rows with no
/// looked-up ids. Raw SQL only before and after, pinned to this migration, for the reasons in <see cref="ReworkBookPositionAnchorMigrationTests"/>.
/// </summary>
public class AddSeriesRelationConnectorMigrationTests : IDisposable
{
    private const string PriorMigration = "20261027211329_AddBookLastExcerpt";
    private const string ThisMigration = "20261029091423_AddSeriesRelationConnector";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_series_connector_migration_test_{Guid.NewGuid():N}.db");

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

    private static int Count(PaperbunkrDbContext context, string sql) => context.Database.SqlQueryRaw<int>(sql).ToList().Single();

    [Fact]
    public void ExistingRows_GetTheConnectorOn_AndNullTimestamps()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw(
                "INSERT INTO Series (Name, ContentType, ReadingMode, Status, ReadingStatus, EmptyRowAcknowledged, TrackerPromptShown) VALUES " +
                "('Superman', 'Unknown', 'LeftToRight', 'Unknown', 'Unknown', 0, 0)");
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);

            // The settings row is created by the app on first run, so check the column's default (what an existing row is filled with).
            Assert.Equal(1, Count(context,
                "SELECT COUNT(*) AS Value FROM pragma_table_info('AppSettings') WHERE name = 'FindRelatedSeriesAutomatically' AND dflt_value IN ('1', 'true')"));
            Assert.Equal(1, Count(context, "SELECT COUNT(*) AS Value FROM Series WHERE Name = 'Superman' AND MetronAssociationsCheckedUtc IS NULL"));
            Assert.Equal(0, Count(context,
                "SELECT COUNT(*) AS Value FROM SeriesAssociations WHERE ComicVineSeriesId IS NOT NULL OR GcdSeriesId IS NOT NULL OR IdsCheckedUtc IS NOT NULL"));
        }
    }
}
