using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <c>AddLocationProfiles</c> (2026-11-11 follow-up to docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3): existing
/// locations keep their rows with no image or profile yet, and rolling back removes only the new location columns. Raw SQL, pinned to this migration.
/// </summary>
public class AddLocationProfilesMigrationTests : IDisposable
{
    private const string PriorMigration = "20261110114835_AddEntityProfiles";
    private const string ThisMigration = "20261111115950_AddLocationProfiles";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_location_profiles_migration_test_{Guid.NewGuid():N}.db");

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
    public void ExistingLocation_KeepsItsRow_AndRollbackRemovesOnlyTheNewColumns()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw("INSERT INTO Locations (Name) VALUES ('Westchester')");
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);
            Assert.Equal(1, Count(context,
                "SELECT COUNT(*) AS Value FROM Locations WHERE Name = 'Westchester' AND ImageUrl IS NULL AND ImageSource = 0 AND ImageCheckedAt IS NULL " +
                "AND CustomImagePath IS NULL AND Description IS NULL AND Aliases IS NULL"));

            context.GetService<IMigrator>().Migrate(PriorMigration);
            Assert.Equal(0, Count(context, "SELECT COUNT(*) AS Value FROM pragma_table_info('Locations') WHERE name = 'ImageUrl'"));
            Assert.Equal(1, Count(context, "SELECT COUNT(*) AS Value FROM Locations WHERE Name = 'Westchester'"));
            Assert.Equal(1, Count(context, "SELECT COUNT(*) AS Value FROM pragma_table_info('Characters') WHERE name = 'ImageUrl'"));
        }
    }
}
