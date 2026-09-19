using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddMatrixRainEnabled</c> migration - a single plain <c>AppSettings</c> bool
/// column, real <c>DropColumn</c> on <c>Down()</c>. EF's own migration scaffold defaulted the
/// column's SQL DEFAULT to <c>false</c> even though the C# property default is <c>true</c> (a real,
/// easy-to-miss mismatch found and fixed by hand while writing this migration - see
/// <see cref="Paperbunkr.Data.Entities.AppSettings.MatrixRainEnabled"/>'s own doc comment) -
/// <see cref="Migration_ColumnDefault_IsTrue_NotFalse"/> checks that fix directly, since a fresh row
/// via <c>GetOrCreateAppSettings()</c> always writes the C# default explicitly and would pass either
/// way, never actually exercising the column's own SQL-level default.
/// </summary>
public class AddMatrixRainEnabledMigrationTests : IDisposable
{
    private const string PriorMigration = "20260918181319_AddTrackerBehaviorSettings";
    private readonly string _dbPath;

    public AddMatrixRainEnabledMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_matrixrain_migration_test_{Guid.NewGuid():N}.db");
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

    /// <summary>
    /// Verifies the column's real SQL-level DEFAULT (what SQLite would backfill into an EXISTING
    /// row on ALTER TABLE ADD COLUMN, i.e. an upgrading user's already-existing AppSettings row) is
    /// <c>1</c> (true), not <c>0</c> (false) - checked directly via <c>pragma_table_info</c>'s
    /// <c>dflt_value</c> rather than by seeding a real pre-migration row: AppSettings has
    /// accumulated enough heterogeneous columns (several DateTime- and enum-backed TEXT columns
    /// among them) that hand- or reflection-building a valid dummy INSERT for every column turned
    /// out fragile and not worth it for what this migration actually needs verified - the DEFAULT
    /// clause SQLite baked into the column itself, which this checks precisely and directly. A
    /// brand-new row (<see cref="Migration_FreshRow_DefaultsToTrue_AndRoundTrips_AndIsReversible"/>)
    /// doesn't exercise this: <c>GetOrCreateAppSettings()</c> always writes the C# default
    /// explicitly, regardless of what the column's own SQL default says.
    /// </summary>
    [Fact]
    public void Migration_ColumnDefault_IsTrue_NotFalse()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        string? dfltValue = context.Database
            .SqlQueryRaw<string>("SELECT dflt_value AS Value FROM pragma_table_info('AppSettings') WHERE name = 'MatrixRainEnabled'")
            .Single();

        Assert.Equal("1", dfltValue);
    }

    [Fact]
    public void Migration_FreshRow_DefaultsToTrue_AndRoundTrips_AndIsReversible()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var settings = context.GetOrCreateAppSettings();
            Assert.True(settings.MatrixRainEnabled);

            settings.MatrixRainEnabled = false;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.False(settings.MatrixRainEnabled);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var columns = context.Database
                .SqlQueryRaw<string>(
                    "SELECT name FROM pragma_table_info('AppSettings') WHERE name = 'MatrixRainEnabled';")
                .ToList();
            Assert.Empty(columns);
        }
    }
}
