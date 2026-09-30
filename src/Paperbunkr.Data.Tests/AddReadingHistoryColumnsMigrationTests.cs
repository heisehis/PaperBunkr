using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddReadingHistoryColumns</c> migration (docs/superpowers/specs/2026-09-29-insights-
/// reading-history-design.md §1) applies cleanly on a full migrate and that the new columns round-trip.
/// Backfill correctness is covered by <see cref="ReadingHistoryBackfillTests"/>. No up-down-up round trip -
/// see memory note on the migration up-down-up test antipattern.
/// </summary>
public class AddReadingHistoryColumnsMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public AddReadingHistoryColumnsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_history_mig_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
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

    [Fact]
    public void FullMigrate_AddsHistoryColumns_WithHiddenDefaultingToFalse()
    {
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Database.Migrate();
            ctx.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Comic,
                ItemId = 1,
                Kind = ReadingEventKind.Opened,
                TimestampUtc = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc),
                SeriesTitle = "Saga",
                ItemLabel = "#1",
            });
            ctx.SaveChanges();

            // A row inserted without the column (as pre-existing rows effectively are) takes the SQL default.
            ctx.Database.ExecuteSqlRaw(
                """INSERT INTO "ReadingEvents" ("ItemType", "ItemId", "Kind", "TimestampUtc") VALUES ('Novel', 2, 'Opened', '2026-09-29 00:00:00');""");
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var rows = ctx.ReadingEvents.AsNoTracking().OrderBy(e => e.Id).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(("Saga", "#1"), (rows[0].SeriesTitle, rows[0].ItemLabel));
            Assert.All(rows, r => Assert.False(r.HiddenFromHistory));
            Assert.Null(rows[1].SeriesTitle);
        }
    }
}
