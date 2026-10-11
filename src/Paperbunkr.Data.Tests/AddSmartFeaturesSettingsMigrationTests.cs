using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The smart-features settings migration (docs/superpowers/specs/2026-10-06-smart-features-design.md §6.1, §7.3, §7.4): an existing
/// library keeps today's behaviour (threshold 0, refresh on), and nothing is marked as already completed.
/// </summary>
public class AddSmartFeaturesSettingsMigrationTests : IDisposable
{
    private const string PriorMigration = "20261006101119_AddReadingEventActiveSeconds";
    private const string ThisMigration = "20261006105543_AddSmartFeaturesSettings";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smart_settings_migration_test_{Guid.NewGuid():N}.db");

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

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private int ColumnCount(string table, string column)
    {
        using var context = CreateContext();
        return context.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info('{table}') WHERE name = '{column}'").Single();
    }

    [Fact]
    public void Migration_GivesAnExistingLibraryTodaysBehaviour_AndRollsBack()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            // Rows that exist before the migration: an app-settings row, a continuity and a story event.
            context.Database.ExecuteSqlRaw(
                "INSERT INTO AppSettings (Id, MinimizeToTray, MinimizeToTrayNoticeShown, NavRailPinned, ReducedMotion) VALUES (1, 0, 0, 0, 0);");
            context.Database.ExecuteSqlRaw("INSERT INTO Continuities (Name, CreatedAt, UpdatedAt) VALUES ('Earth-616', '2026-01-01 00:00:00', '2026-01-01 00:00:00');");
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);
        }

        using (var context = CreateContext())
        {
            // Read through raw SQL: once later migrations exist, the model is ahead of this one.
            Assert.Equal(1, context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM AppSettings WHERE CAST(AutoApplyMinConfidence AS REAL) = 0 AND RefreshProviderDataOnComplete = 1").Single());
            Assert.Equal(1, context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Continuities WHERE CompletedNotifiedAt IS NULL").Single());
        }

        Assert.Equal(1, ColumnCount("StoryEvents", "CompletedNotifiedAt"));

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
        }

        Assert.Equal(0, ColumnCount("Continuities", "CompletedNotifiedAt"));
        Assert.Equal(0, ColumnCount("StoryEvents", "CompletedNotifiedAt"));

        // The two AppSettings columns stay on the way down, on purpose: dropping a column rebuilds AppSettings on SQLite and would
        // strand the legacy columns older rollbacks still need (the project's standing rule for AppSettings columns).
        Assert.Equal(1, ColumnCount("AppSettings", "AutoApplyMinConfidence"));
        Assert.Equal(1, ColumnCount("AppSettings", "RefreshProviderDataOnComplete"));
    }

    [Fact]
    public void TheSettings_RoundTripThroughTheModel()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            Assert.Equal(0m, settings.AutoApplyMinConfidence);
            Assert.True(settings.RefreshProviderDataOnComplete);
            settings.AutoApplyMinConfidence = 0.65m;
            settings.RefreshProviderDataOnComplete = false;
            context.SaveChanges();
        }

        using var fresh = CreateContext();
        var saved = fresh.GetOrCreateAppSettings();
        Assert.Equal(0.65m, saved.AutoApplyMinConfidence);
        Assert.False(saved.RefreshProviderDataOnComplete);
    }
}
