using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>LastContentTypeSweepUtc</c> migration (docs/superpowers/specs/
/// 2026-08-30-publisher-content-type-classification-design.md) - a plain nullable column add, no
/// data fix. Guards against the scaffolder emitting anything other than a clean
/// <c>AddColumn</c>/<c>DropColumn</c>.
/// </summary>
public class AddLastContentTypeSweepUtcMigrationTests : IDisposable
{
    private const string PriorMigration = "20260902131056_AddFb2MobiBookFormat";
    private readonly string _dbPath;

    public AddLastContentTypeSweepUtcMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_contenttypesweep_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsNullableColumn_ThatRoundTrips_AndIsReversible()
    {
        // Up to HEAD: the singleton settings row gets a nullable LastContentTypeSweepUtc, default null.
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            Assert.Null(settings.LastContentTypeSweepUtc);

            settings.LastContentTypeSweepUtc = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), context.GetOrCreateAppSettings().LastContentTypeSweepUtc);
        }

        // Down one step: the column is dropped, the singleton row survives SQLite's table rebuild.
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var columns = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AppSettings') WHERE name = 'LastContentTypeSweepUtc';")
                .ToList();
            Assert.Empty(columns);

            var rowCount = context.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            Assert.Equal(1, rowCount);
        }
    }
}
