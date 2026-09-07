using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddLibraryHealthConfirmedMissingThreshold</c> migration (docs/superpowers/
/// specs/2026-09-07-library-health-redesign-design.md §7): a plain int column on AppSettings,
/// default 2 (preserving the prior hardcoded <c>LibraryHealthService.ConfirmedMissingThreshold</c>
/// constant). <c>Down</c> is a deliberate no-op, same reasoning as
/// <c>AddMissingVerificationCountAndRemovedLibraryEntry</c>'s column adds - a <c>DropColumn</c> on
/// AppSettings would trigger SQLite's full-table rebuild and risk silently dropping other
/// already-orphaned columns.
/// </summary>
public class AddLibraryHealthConfirmedMissingThresholdMigrationTests : IDisposable
{
    private const string PriorMigration = "20260907060613_ReworkBookPositionAnchor";
    private readonly string _dbPath;

    public AddLibraryHealthConfirmedMissingThresholdMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_confirmedmissingthreshold_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumn_WithDefaultTwo_ThatRoundTrips()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        Assert.Equal(2, context.GetOrCreateAppSettings().LibraryHealthConfirmedMissingThreshold);

        context.GetOrCreateAppSettings().LibraryHealthConfirmedMissingThreshold = 4;
        context.SaveChanges();

        using var reopened = CreateContext();
        Assert.Equal(4, reopened.GetOrCreateAppSettings().LibraryHealthConfirmedMissingThreshold);
    }

    [Fact]
    public void Down_LeavesTheColumnAsAnOrphan()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.GetService<IMigrator>().Migrate(PriorMigration);

        var columns = context.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AppSettings') WHERE name = 'LibraryHealthConfirmedMissingThreshold';")
            .ToList();
        Assert.Single(columns);
    }
}
