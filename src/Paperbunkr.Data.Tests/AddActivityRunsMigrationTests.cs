using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddActivityRuns</c> migration (docs/superpowers/specs/2026-09-03-activity-
/// center-design.md) - a single new <c>ActivityRuns</c> table with a <c>StartedUtc</c> index, no
/// data fix. Guards against the scaffolder emitting anything other than a clean
/// <c>CreateTable</c>/<c>DropTable</c> and confirms a clean round-trip / reversal.
/// </summary>
public class AddActivityRunsMigrationTests : IDisposable
{
    private const string PriorMigration = "20260903211057_AddMetadataWriteBackSettings";
    private readonly string _dbPath;

    public AddActivityRunsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_activityruns_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_CreatesActivityRunsTable_ThatRoundTrips()
    {
        var started = new DateTime(2026, 9, 3, 14, 2, 0, DateTimeKind.Utc);
        var finished = new DateTime(2026, 9, 3, 14, 5, 0, DateTimeKind.Utc);

        using (var context = CreateContext())
        {
            context.Database.Migrate();
            Assert.Empty(context.ActivityRuns.ToList());

            context.ActivityRuns.Add(new ActivityRun
            {
                Kind = ActivityJobKind.Import,
                Title = "Import 60 dropped files",
                Trigger = ActivityTrigger.DragDrop,
                StartedUtc = started,
                FinishedUtc = finished,
                Status = ActivityRunStatus.Failed,
                ResultSummary = "48 imported, 12 failed",
                ResultLinkKind = "LibrarySavedFilter",
                ResultLinkPayload = "failed:1,2,3",
                ItemsProcessed = 48,
                ItemsFailed = 12,
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var run = context.ActivityRuns.Single();
            Assert.Equal(ActivityJobKind.Import, run.Kind);
            Assert.Equal(ActivityTrigger.DragDrop, run.Trigger);
            Assert.Equal(ActivityRunStatus.Failed, run.Status);
            Assert.Equal(started, run.StartedUtc);
            Assert.Equal(finished, run.FinishedUtc);
            Assert.Equal("48 imported, 12 failed", run.ResultSummary);
            Assert.Equal("LibrarySavedFilter", run.ResultLinkKind);
            Assert.Equal(12, run.ItemsFailed);

            // Enum columns are stored as their string name (this context's convention).
            var kindText = context.Database
                .SqlQueryRaw<string>("SELECT Kind AS Value FROM ActivityRuns LIMIT 1")
                .Single();
            Assert.Equal("Import", kindText);

            var indexes = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_index_list('ActivityRuns') WHERE \"unique\" = 0;")
                .ToList();
            Assert.Contains("IX_ActivityRuns_StartedUtc", indexes);
        }
    }

    [Fact]
    public void Migration_IsReversible_DroppingTheTable()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var table = context.Database
                .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name='ActivityRuns';")
                .ToList();
            Assert.Empty(table);
        }
    }
}
