using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddReadingGoals</c> migration (docs/superpowers/specs/2026-09-23-insights-reading-
/// goals-design.md) applies cleanly on a full migrate and that the new schema round-trips. A full
/// down-migrate is deliberately not tested here, same reasoning as <see cref="AddLibrarySnapshotsMigrationTests"/>.
/// </summary>
public class AddReadingGoalsMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public AddReadingGoalsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_readinggoal_mig_{Guid.NewGuid():N}.db");
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
    public void FullMigrate_CreatesReadingGoalsTable_AndRoundTripsARow()
    {
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Database.Migrate();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.ReadingGoals.Add(new ReadingGoal
            {
                Title = "Read 50 issues this year",
                Metric = GoalMetric.Items,
                Target = 50,
                PeriodKind = GoalPeriodKind.ThisYear,
                PeriodStart = new DateOnly(2026, 1, 1),
                PeriodEnd = new DateOnly(2026, 12, 31),
                ScopeKind = GoalScopeKind.Library,
                ScopeValue = null,
                CreatedUtc = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc),
            });
            ctx.SaveChanges();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var row = ctx.ReadingGoals.Single();
            Assert.Equal("Read 50 issues this year", row.Title);
            Assert.Equal(GoalMetric.Items, row.Metric);
            Assert.Equal(50, row.Target);
            Assert.Equal(GoalPeriodKind.ThisYear, row.PeriodKind);
            Assert.Equal(new DateOnly(2026, 1, 1), row.PeriodStart);
            Assert.Equal(new DateOnly(2026, 12, 31), row.PeriodEnd);
            Assert.Equal(GoalScopeKind.Library, row.ScopeKind);
            Assert.Null(row.ScopeValue);
        }
    }
}
