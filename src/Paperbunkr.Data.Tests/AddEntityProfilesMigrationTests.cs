using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <c>AddEntityProfiles</c> (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-plan.md D1.1/D1.2): existing characters, teams and
/// creators keep their rows with no image or profile yet, the index version starts at 0 (so the rebuild task runs once), and rolling back
/// leaves the AppSettings column in place (the rollback-chain rule). Raw SQL only, pinned to this migration, for the reasons in
/// <see cref="ReworkBookPositionAnchorMigrationTests"/>.
/// </summary>
public class AddEntityProfilesMigrationTests : IDisposable
{
    private const string PriorMigration = "20261106153208_AddCollectionBannerSlim";
    private const string ThisMigration = "20261110114835_AddEntityProfiles";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_entity_profiles_migration_test_{Guid.NewGuid():N}.db");

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
    public void ExistingEntities_KeepTheirRows_WithNoProfileYet()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw("INSERT INTO Characters (Name) VALUES ('Storm')");
            context.Database.ExecuteSqlRaw("INSERT INTO Teams (Name) VALUES ('X-Men')");
            context.Database.ExecuteSqlRaw("INSERT INTO Creators (Name) VALUES ('Chris Claremont')");
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);

            foreach (string table in new[] { "Characters", "Teams", "Creators" })
            {
                Assert.Equal(1, Count(context,
                    $"SELECT COUNT(*) AS Value FROM {table} WHERE ImageUrl IS NULL AND ImageSource = 0 AND ImageCheckedAt IS NULL " +
                    "AND CustomImagePath IS NULL AND Description IS NULL AND Aliases IS NULL"));
            }
        }
    }

    [Fact]
    public void IndexVersion_StartsAtZero_AndSurvivesRollback()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);
            // Raw SQL only: the current model may carry columns from later migrations, so GetOrCreateAppSettings cannot be used at this point.
            Assert.Equal(1, Count(context,
                "SELECT COUNT(*) AS Value FROM pragma_table_info('AppSettings') WHERE name = 'EntityIndexVersion' AND \"notnull\" = 1 AND dflt_value = '0'"));

            context.GetService<IMigrator>().Migrate(PriorMigration);

            Assert.Equal(1, Count(context, "SELECT COUNT(*) AS Value FROM pragma_table_info('AppSettings') WHERE name = 'EntityIndexVersion'"));
            Assert.Equal(0, Count(context, "SELECT COUNT(*) AS Value FROM pragma_table_info('Characters') WHERE name = 'ImageUrl'"));
        }
    }
}
