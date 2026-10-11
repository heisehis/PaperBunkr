using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <c>ReadingEvent.ActiveSeconds</c> (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.1): added nullable with no backfill,
/// so every row written before it stays null and is ignored by the pace calculation.
/// </summary>
public class AddReadingEventActiveSecondsMigrationTests : IDisposable
{
    private const string PriorMigration = "20261006090932_AddHealthFindingDismissals";
    private const string ThisMigration = "20261006101119_AddReadingEventActiveSeconds";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_active_seconds_migration_test_{Guid.NewGuid():N}.db");

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

    private bool ColumnExists()
    {
        using var context = CreateContext();
        return context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('ReadingEvents') WHERE name = 'ActiveSeconds'").Single() == 1;
    }

    [Fact]
    public void Migration_AddsANullableColumn_LeavesOldRowsNull_AndRollsBack()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw(
                "INSERT INTO ReadingEvents (ItemType, ItemId, Kind, TimestampUtc, PagesRead, HiddenFromHistory) VALUES (0, 1, 0, '2026-01-01 00:00:00', 12, 0);");
        }

        Assert.False(ColumnExists());

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);
        }

        Assert.True(ColumnExists());
        using (var context = CreateContext())
        {
            // Read through raw SQL: the model may be ahead of this migration once later ones exist.
            Assert.Equal(1, context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ReadingEvents WHERE ActiveSeconds IS NULL AND PagesRead = 12").Single());
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
        }

        Assert.False(ColumnExists());
    }

    [Fact]
    public void ActiveSeconds_RoundTripsThroughTheModel()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        context.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 1, Kind = ReadingEventKind.Opened, TimestampUtc = DateTime.UtcNow, PagesRead = 20, ActiveSeconds = 600 });
        context.SaveChanges();

        using var fresh = CreateContext();
        Assert.Equal(600, fresh.ReadingEvents.Single().ActiveSeconds);
    }
}
