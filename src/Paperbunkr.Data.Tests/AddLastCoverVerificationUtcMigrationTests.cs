using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>LastCoverVerificationUtc</c> migration (docs/superpowers/specs/2026-08-30-cover-
/// thumbnail-content-verification-design.md) - a plain nullable column add, no data fix. <c>Down</c>
/// is a deliberate no-op (see the migration's comment): a <c>DropColumn</c> here would trigger
/// SQLite's full-table rebuild, silently dropping the orphaned <c>LibraryGroupField</c> family of
/// columns and breaking later <c>Down()</c> steps in a rollback chain.
/// </summary>
public class AddLastCoverVerificationUtcMigrationTests : IDisposable
{
    private const string PriorMigration = "20260905042026_AddNavRailHoverExpandEnabled";
    private readonly string _dbPath;

    public AddLastCoverVerificationUtcMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_coververification_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsNullableColumn_ThatRoundTrips_AndDownIsANoOp()
    {
        // Up to HEAD: the singleton settings row gets a nullable LastCoverVerificationUtc, default null.
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            Assert.Null(settings.LastCoverVerificationUtc);

            settings.LastCoverVerificationUtc = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), context.GetOrCreateAppSettings().LastCoverVerificationUtc);
        }

        // Down one step: Down() is a deliberate no-op, so the column stays put (left as an orphan)
        // and the singleton row is untouched.
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var columns = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AppSettings') WHERE name = 'LastCoverVerificationUtc';")
                .ToList();
            Assert.Single(columns);

            var rowCount = context.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            Assert.Equal(1, rowCount);
        }
    }
}
