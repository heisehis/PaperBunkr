using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Scheduling;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="ScheduledRunStore"/> - seeding + the three tasks that mirror pre-existing
/// <see cref="AppSettings"/> columns
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// </summary>
public class ScheduledRunStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ScheduledRunStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_runstore_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var seed = new PaperbunkrDbContext(_dbOptions);
        seed.Database.EnsureCreated();
        seed.GetOrCreateAppSettings();
        seed.SaveChanges();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private ScheduledRunStore Store() => new(() => new PaperbunkrDbContext(_dbOptions));

    [Fact]
    public void SeedMissing_CreatesOneRowPerCatalogTask_Once()
    {
        var store = Store();
        store.SeedMissing(ScheduledTaskCatalog.All);
        store.SeedMissing(ScheduledTaskCatalog.All); // idempotent

        using var ctx = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(ScheduledTaskCatalog.All.Count, ctx.ScheduledTaskStates.Count());
    }

    [Fact]
    public void DbBackupTask_MirrorsAutoBackupSettings_OnReadAndWrite()
    {
        var store = Store();
        store.SeedMissing(ScheduledTaskCatalog.All);

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var s = ctx.GetOrCreateAppSettings();
            s.AutoBackupEnabled = false;
            s.AutoBackupMinIntervalHours = 9;
            ctx.SaveChanges();
        }

        var row = store.Load(ScheduledTaskCatalog.DbBackup)!;
        Assert.False(row.Enabled);
        Assert.Equal(9, row.IntervalHours);

        store.SetEnabled(ScheduledTaskCatalog.DbBackup, true);
        store.SetSchedule(ScheduledTaskCatalog.DbBackup, ScheduleMode.Interval, 5, 0);

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var s = ctx.GetOrCreateAppSettings();
            Assert.True(s.AutoBackupEnabled);
            Assert.Equal(5, s.AutoBackupMinIntervalHours);
        }
    }

    [Fact]
    public void VerifyCoversTask_RecordRun_MirrorsLastCoverVerificationUtc()
    {
        var store = Store();
        store.SeedMissing(ScheduledTaskCatalog.All);
        var when = new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc);

        store.RecordRun(ScheduledTaskCatalog.VerifyCovers, ScheduledRunStatus.Succeeded, when);

        using var ctx = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(when, ctx.GetOrCreateAppSettings().LastCoverVerificationUtc);
    }

    [Fact]
    public void RecordRun_Skipped_DoesNothing()
    {
        var store = Store();
        store.SeedMissing(ScheduledTaskCatalog.All);

        store.RecordRun(ScheduledTaskCatalog.LibraryScan, ScheduledRunStatus.Skipped, DateTime.UtcNow);

        Assert.Null(store.Load(ScheduledTaskCatalog.LibraryScan)!.LastRunUtc);
    }
}
