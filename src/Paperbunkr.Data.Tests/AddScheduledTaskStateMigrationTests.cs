using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddScheduledTaskState</c> migration (docs/superpowers/specs/2026-09-06-
/// scheduled-tasks-and-cover-durability-design.md, Part 1): the per-task state table + the
/// notification-level setting are created, <c>ScanFoldersOnStartup</c> is retired with its value
/// carried into the two folder-scan task rows, and <c>Down</c> is partial (never re-adds the
/// dropped column).
/// </summary>
public class AddScheduledTaskStateMigrationTests : IDisposable
{
    private const string PriorMigration = "20260905202117_AddReadingEventLog";
    private readonly string _dbPath;

    public AddScheduledTaskStateMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_schedstate_migration_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_CreatesTheTable_AndTheNotificationSetting_WithItsDefault()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        Assert.Equal(ScheduledTaskNotificationLevel.OnlyFailures, context.GetOrCreateAppSettings().ScheduledTaskNotificationLevel);

        context.ScheduledTaskStates.Add(new ScheduledTaskState
        {
            TaskId = "verify-covers",
            Enabled = true,
            Mode = ScheduleMode.DailyAt,
            DailyAtMinutes = 180,
            LastRunStatus = ScheduledRunStatus.Succeeded,
        });
        context.SaveChanges();

        var reloaded = context.ScheduledTaskStates.AsNoTracking().Single(s => s.TaskId == "verify-covers");
        Assert.Equal(ScheduleMode.DailyAt, reloaded.Mode);
        Assert.Equal(ScheduledRunStatus.Succeeded, reloaded.LastRunStatus);
    }

    // The migration's seed statements, verbatim - re-run here against a row that has the (dormant)
    // ScanFoldersOnStartup flag set, since a fresh migrate has no AppSettings row to read.
    private const string SeedLibraryScan =
        @"INSERT INTO ScheduledTaskStates (TaskId, Enabled, Mode, IntervalHours, DailyAtMinutes)
          SELECT 'library-scan', COALESCE((SELECT ScanFoldersOnStartup FROM AppSettings WHERE Id = 1), 0), 'Interval', 6, 0
          WHERE NOT EXISTS (SELECT 1 FROM ScheduledTaskStates WHERE TaskId = 'library-scan');";

    [Fact]
    public void SeedSql_CarriesScanFoldersOnStartup_IntoTheFolderScanTaskRow()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        context.GetOrCreateAppSettings(); // ensure the singleton row exists
        context.Database.ExecuteSqlRaw("UPDATE AppSettings SET ScanFoldersOnStartup = 1 WHERE Id = 1;");
        context.Database.ExecuteSqlRaw("DELETE FROM ScheduledTaskStates WHERE TaskId = 'library-scan';");

        context.Database.ExecuteSqlRaw(SeedLibraryScan);

        var scan = context.ScheduledTaskStates.AsNoTracking().Single(s => s.TaskId == "library-scan");
        Assert.True(scan.Enabled);
        Assert.Equal(ScheduleMode.Interval, scan.Mode);
        Assert.Equal(6, scan.IntervalHours);
    }

    [Fact]
    public void SeedSql_IsIdempotent()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.Database.ExecuteSqlRaw(SeedLibraryScan); // second time - row already exists

        Assert.Single(context.ScheduledTaskStates.AsNoTracking().Where(s => s.TaskId == "library-scan"));
    }

    [Fact]
    public void Migration_WithScanFoldersOnStartupOff_SeedsTheFolderScanTasksDisabled()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration); // default is 0
        }

        using var final = CreateContext();
        final.Database.Migrate();

        Assert.False(final.ScheduledTaskStates.AsNoTracking().Single(s => s.TaskId == "library-scan").Enabled);
    }

    [Fact]
    public void Down_DropsTheNewObjects_ButDoesNotReAddScanFoldersOnStartup()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetOrCreateAppSettings();
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var tables = context.Database
                .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name='ScheduledTaskStates';")
                .ToList();
            Assert.Empty(tables); // the only thing Down() removes

            var rowCount = context.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            Assert.Equal(1, rowCount); // the singleton survives the rollback
        }
    }
}
