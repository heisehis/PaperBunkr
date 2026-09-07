using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddScanMissingFileHandling</c> migration (docs/superpowers/specs/2026-09-06-
/// scan-missing-file-handling-design.md): two plain bool columns on AppSettings and a brand-new
/// RemovedFilePaths table. <c>Down</c> drops the new table but is a deliberate no-op for the two
/// column adds (see the migration's own comment) - same standing rule as
/// <c>AddMissingVerificationCountAndRemovedLibraryEntry</c>'s Down: a <c>DropColumn</c> on
/// AppSettings would trigger SQLite's full-table rebuild, silently dropping the orphaned
/// <c>LibraryGroupField</c> family of columns and breaking later <c>Down()</c> steps in a rollback
/// chain.
/// </summary>
public class AddScanMissingFileHandlingMigrationTests : IDisposable
{
    private const string PriorMigration = "20260906153921_AddMissingVerificationCountAndRemovedLibraryEntry";
    private readonly string _dbPath;

    public AddScanMissingFileHandlingMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_scanmissing_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumnsAndTable_ThatRoundTrip()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var settings = context.GetOrCreateAppSettings();
        Assert.False(settings.AutoRemoveMissingOnScan);
        Assert.False(settings.DontReimportRemovedFiles);

        settings.AutoRemoveMissingOnScan = true;
        settings.DontReimportRemovedFiles = true;
        context.RemovedFilePaths.Add(new RemovedFilePath { FilePath = @"C:\Comics\test.cbz", RemovedAtUtc = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc) });
        context.SaveChanges();

        using var reopened = CreateContext();
        var reopenedSettings = reopened.GetOrCreateAppSettings();
        Assert.True(reopenedSettings.AutoRemoveMissingOnScan);
        Assert.True(reopenedSettings.DontReimportRemovedFiles);
        var entry = reopened.RemovedFilePaths.Single();
        Assert.Equal(@"C:\Comics\test.cbz", entry.FilePath);
    }

    [Fact]
    public void Migration_RemovedFilePathIsUnique()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.RemovedFilePaths.Add(new RemovedFilePath { FilePath = @"C:\Comics\test.cbz", RemovedAtUtc = DateTime.UtcNow });
        context.SaveChanges();

        context.RemovedFilePaths.Add(new RemovedFilePath { FilePath = @"C:\Comics\test.cbz", RemovedAtUtc = DateTime.UtcNow });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void Down_DropsRemovedFilePathsTable_ButLeavesTheTwoColumnsAsOrphans()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.GetService<IMigrator>().Migrate(PriorMigration);

        var autoRemoveColumn = context.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AppSettings') WHERE name = 'AutoRemoveMissingOnScan';")
            .ToList();
        Assert.Single(autoRemoveColumn);

        var dontReimportColumn = context.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AppSettings') WHERE name = 'DontReimportRemovedFiles';")
            .ToList();
        Assert.Single(dontReimportColumn);

        var tables = context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'RemovedFilePaths';")
            .ToList();
        Assert.Empty(tables);
    }
}
