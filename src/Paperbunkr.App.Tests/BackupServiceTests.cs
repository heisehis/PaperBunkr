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
}
