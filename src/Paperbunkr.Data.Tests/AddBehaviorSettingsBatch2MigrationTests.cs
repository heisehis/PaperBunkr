using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddBehaviorSettingsBatch2</c> migration (docs/superpowers/specs/2026-09-04-
/// behavior-settings-batch2-design.md) - four bool columns on the <c>AppSettings</c> singleton.
/// The two behaviors Paperbunkr already does unconditionally (<c>RestoreSessionOnStartup</c>,
/// <c>EnableDragDropImport</c>) default <c>true</c> so an existing row keeps working the same; the
/// two new opt-in behaviors default <c>false</c>. <c>Down</c> is a deliberate no-op.
/// </summary>
public class AddBehaviorSettingsBatch2MigrationTests : IDisposable
{
    private const string PriorMigration = "20260903215515_AddActivityRuns";
    private readonly string _dbPath;

    public AddBehaviorSettingsBatch2MigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_behaviorbatch2_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsFourColumns_WithBehaviorPreservingDefaults_ThatRoundTrip()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();

            Assert.True(settings.RestoreSessionOnStartup);
            Assert.True(settings.EnableDragDropImport);
            Assert.False(settings.PromptReviewOnFinish);

            // ScanFoldersOnStartup was retired by AddScheduledTaskState (the folder-scan tasks
            // replace it), so this "migrate to HEAD" run no longer sees that column.

            settings.RestoreSessionOnStartup = false;
            settings.PromptReviewOnFinish = true;
            settings.EnableDragDropImport = false;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.False(settings.RestoreSessionOnStartup);
            Assert.True(settings.PromptReviewOnFinish);
            Assert.False(settings.EnableDragDropImport);
        }
    }

    [Fact]
    public void Migration_Down_IsANoOp_LeavingTheColumnsAndRowInPlace()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetOrCreateAppSettings(); // the singleton row must survive down-migration
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            // Down() is deliberately empty (see the migration's comment), so its own columns stay
            // put. ScanFoldersOnStartup is absent here because a *later* migration
            // (AddScheduledTaskState) dropped it and - by the orphan-column rule - never re-adds it.
            var columns = context.Database
                .SqlQueryRaw<string>(
                    "SELECT name FROM pragma_table_info('AppSettings') WHERE name IN " +
                    "('RestoreSessionOnStartup','PromptReviewOnFinish','EnableDragDropImport');")
                .ToList();
            Assert.Equal(3, columns.Count);

            var rowCount = context.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            Assert.Equal(1, rowCount);
        }
    }
}
