using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddGoalScopes</c> migration (docs/superpowers/specs/2026-10-04-insights-goal-scopes-design.md): a goal's legacy single scope
/// is carried into a <see cref="ReadingGoalScope"/> row, and the new schema round-trips. A full down-migrate is deliberately not tested, same
/// reasoning as <see cref="AddLibrarySnapshotsMigrationTests"/> (its <c>Down</c> only drops the new table, by design).
/// </summary>
public class AddGoalScopesMigrationTests : IDisposable
{
    private const string ThisMigration = "AddGoalScopes";

    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public AddGoalScopesMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_goalscopes_mig_{Guid.NewGuid():N}.db");
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
    public void LegacyScopes_BecomeScopeRows_AndLibraryGoalsGetNone()
    {
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            // Migrate to the migration just before this one, so the goals below exist in the old (single-scope) shape.
            var all = ctx.Database.GetMigrations().ToList();
            string previous = all[all.FindIndex(m => m.EndsWith(ThisMigration)) - 1];
            ctx.Database.GetService<IMigrator>().Migrate(previous);

            ctx.Database.ExecuteSqlRaw(
                "INSERT INTO ReadingGoals (Title, Metric, Target, PeriodKind, PeriodStart, PeriodEnd, ScopeKind, ScopeValue, CreatedUtc) VALUES " +
                "('Marvel goal', 0, 20, 0, '2026-01-01', '2026-12-31', 2, 'Marvel', '2026-09-01 00:00:00'), " +
                "('Whole library', 0, 50, 0, '2026-01-01', '2026-12-31', 0, NULL, '2026-09-01 00:00:00')");
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Database.Migrate();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var marvel = ctx.ReadingGoals.Include(g => g.Scopes).Single(g => g.Title == "Marvel goal");
            var scope = Assert.Single(marvel.Scopes);
            Assert.Equal(GoalScopeKind.Publisher, scope.Kind);
            Assert.Equal("Marvel", scope.Value);
            Assert.Equal("Marvel", scope.Label);
            Assert.Equal(GoalKind.Count, marvel.Kind); // existing goals stay count goals
            Assert.False(marvel.DistinctOnly);

            Assert.Empty(ctx.ReadingGoals.Include(g => g.Scopes).Single(g => g.Title == "Whole library").Scopes);
        }
    }

    [Fact]
    public void FullMigrate_RoundTripsAFinishGoalWithScopes_AndCascadesTheScopeRows()
    {
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Database.Migrate();
            ctx.ReadingGoals.Add(new ReadingGoal
            {
                Title = "Finish Civil War",
                Kind = GoalKind.Finish,
                Metric = GoalMetric.Items,
                PeriodKind = GoalPeriodKind.NoDeadline,
                PeriodStart = new DateOnly(2026, 10, 4),
                PeriodEnd = DateOnly.MaxValue,
                CreatedUtc = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
                Scopes =
                {
                    new ReadingGoalScope { Kind = GoalScopeKind.ReadingList, Value = "7", Label = "Civil War" },
                    new ReadingGoalScope { Kind = GoalScopeKind.Publisher, Value = "Marvel", Label = "Marvel" },
                },
            });
            ctx.SaveChanges();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var goal = ctx.ReadingGoals.Include(g => g.Scopes).Single();
            Assert.Equal(GoalKind.Finish, goal.Kind);
            Assert.Equal(GoalPeriodKind.NoDeadline, goal.PeriodKind);
            Assert.Equal(DateOnly.MaxValue, goal.PeriodEnd);
            Assert.Equal(2, goal.Scopes.Count);

            ctx.ReadingGoals.Remove(goal);
            ctx.SaveChanges();
            Assert.Empty(ctx.ReadingGoalScopes);
        }
    }
}
