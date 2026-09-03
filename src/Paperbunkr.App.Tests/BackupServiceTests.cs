using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BackupService"/> (docs/superpowers/specs/2026-08-07-preferences-advanced-tab-design.md
/// §3) against a temp SQLite file redirected via <see cref="PaperbunkrDbContext.DatabasePathOverride"/>
/// (the same seam <see cref="ReaderScreenViewModelTests"/> uses) plus a temp backup folder - never
/// touches the real per-user database or backup location. Joins <see cref="AvaloniaTestCollection"/>
/// purely to serialize against every other test class that also mutates the shared static
/// <c>DatabasePathOverride</c> (confirmed necessary - without this, running the full suite raced
/// this class against <see cref="ReaderScreenViewModelTests"/>/<see cref="PreferencesScreenViewModelTests"/>
/// and intermittently failed with "file not found" mid-test).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BackupServiceTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly string _backupRoot;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public BackupServiceTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_backup_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        _backupRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_backup_root_{Guid.NewGuid():N}");

        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
        context.GetOrCreateAppSettings().BackupLocation = _backupRoot;
        context.SaveChanges();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_backupRoot)) Directory.Delete(_backupRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private BackupService CreateService() => new(() => new PaperbunkrDbContext(_dbOptions));

    [Fact]
    public void BackupNow_CopiesLiveDatabase_ToConfiguredLocation()
    {
        var service = CreateService();

        string backupPath = service.BackupNow();

        Assert.True(File.Exists(backupPath));
        Assert.Equal(_backupRoot, Path.GetDirectoryName(backupPath));
        Assert.Equal(new FileInfo(_dbPath).Length, new FileInfo(backupPath).Length);
    }

    [Fact]
    public void GetAvailableBackups_ListsNewestFirst()
    {
        var service = CreateService();
        string first = service.BackupNow();
        Thread.Sleep(1100); // filename timestamp granularity is 1 second
        string second = service.BackupNow();

        var backups = service.GetAvailableBackups();

        Assert.Equal(second, backups[0]);
        Assert.Equal(first, backups[1]);
    }

    [Fact]
    public void BackupNow_PrunesOldestBeyondBackupsToKeep()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().BackupsToKeep = 2;
            context.SaveChanges();
        }

        var service = CreateService();
        service.BackupNow();
        Thread.Sleep(1100);
        service.BackupNow();
        Thread.Sleep(1100);
        service.BackupNow();

        Assert.Equal(2, service.GetAvailableBackups().Count);
    }

    [Fact]
    public void RestoreBackup_OverwritesLiveDatabaseWithBackupContent()
    {
        var service = CreateService();
        string backupPath = service.BackupNow();

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Series.Add(new Data.Entities.Series { Name = "Added After Backup" });
            context.SaveChanges();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        service.RestoreBackup(backupPath);

        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(verify.Series);
    }

    [Fact]
    public void GetBackupLocation_DefaultsToAppDataFolder_WhenNotConfigured()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().BackupLocation = null;
            context.SaveChanges();
        }

        string location = CreateService().GetBackupLocation();

        Assert.Contains("Paperbunkr", location);
        Assert.Contains("backups", location);
    }

    /// <summary>
    /// Regression: when the database can't be opened - the exact situation the recovery flow runs in
    /// (App.HandleDatabaseRecovery → GetAvailableBackups → GetBackupLocation) - this must fall back to
    /// the default backups folder, not throw. A throwing SqliteException here previously crashed the
    /// process before DatabaseRecoveryWindow could appear (real incident 2026-09-03).
    /// </summary>
    [Fact]
    public void GetBackupLocation_And_GetAvailableBackups_FallBackToDefault_WhenTheDatabaseIsUnreadable()
    {
        var service = new BackupService(() => throw new Microsoft.Data.Sqlite.SqliteException("database disk image is malformed", 11));

        string location = service.GetBackupLocation();
        Assert.Contains("Paperbunkr", location);
        Assert.Contains("backups", location);

        // GetAvailableBackups goes through GetBackupLocation - must also not throw.
        var backups = service.GetAvailableBackups();
        Assert.NotNull(backups);
    }

    /// <summary>
    /// Proves the checkpoint-before-copy fix (docs/superpowers/specs/2026-08-29-db-corruption-
    /// safeguards-design.md §2) actually closes the WAL gap, not just the happy path where nothing
    /// was pending: puts the live db into WAL mode, writes a row via a second connection that stays
    /// open (so the write sits in the -wal sidecar rather than the main file), then asserts
    /// <see cref="BackupService.BackupNow"/>'s output has the row anyway.
    /// </summary>
    [Fact]
    public void BackupNow_IncludesRecentWrites_StillPendingInWalFile()
    {
        using (var setup = new PaperbunkrDbContext(_dbOptions))
        {
            setup.Database.ExecuteSqlRaw("PRAGMA journal_mode = 'WAL';");
        }

        using var pendingWriter = new PaperbunkrDbContext(_dbOptions);
        pendingWriter.Database.OpenConnection();
        pendingWriter.Database.ExecuteSqlRaw("PRAGMA journal_mode = 'WAL';");
        pendingWriter.Series.Add(new Data.Entities.Series { Name = "Pending In WAL" });
        pendingWriter.SaveChanges();

        var service = CreateService();
        string backupPath = service.BackupNow();

        var backupOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={backupPath}").Options;
        using var backupContext = new PaperbunkrDbContext(backupOptions);
        Assert.Contains(backupContext.Series, s => s.Name == "Pending In WAL");
    }

    [Fact]
    public void RunAutoBackupIfDue_DoesNothing_WhenAutoBackupDisabled()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().AutoBackupEnabled = false;
            context.SaveChanges();
        }

        CreateService().RunAutoBackupIfDue();

        Assert.Empty(CreateService().GetAvailableBackups());
    }

    [Fact]
    public void RunAutoBackupIfDue_RunsBackup_WhenNoneExistYet()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().AutoBackupEnabled = true;
            context.SaveChanges();
        }

        CreateService().RunAutoBackupIfDue();

        Assert.Single(CreateService().GetAvailableBackups());
    }

    [Fact]
    public void RunAutoBackupIfDue_Skips_WhenNewestBackupIsUnderMinInterval()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.AutoBackupEnabled = true;
            settings.AutoBackupMinIntervalHours = 4;
            context.SaveChanges();
        }

        var service = CreateService();
        service.BackupNow();

        service.RunAutoBackupIfDue();

        Assert.Single(service.GetAvailableBackups());
    }

    [Fact]
    public void RunAutoBackupIfDue_Runs_WhenNewestBackupIsOlderThanMinInterval()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.AutoBackupEnabled = true;
            settings.AutoBackupMinIntervalHours = 4;
            context.SaveChanges();
        }

        var service = CreateService();
        string oldBackup = service.BackupNow();
        string agedStamp = DateTime.UtcNow.AddHours(-5).ToString("yyyyMMdd_HHmmss");
        string agedName = Path.Combine(Path.GetDirectoryName(oldBackup)!, $"paperbunkr_backup_{agedStamp}.db");
        File.Move(oldBackup, agedName);

        service.RunAutoBackupIfDue();

        Assert.Equal(2, service.GetAvailableBackups().Count);
    }
}
