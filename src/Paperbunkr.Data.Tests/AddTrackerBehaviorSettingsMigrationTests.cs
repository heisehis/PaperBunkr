using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddTrackerBehaviorSettings</c> migration (docs/superpowers/specs/2026-09-18-
/// tracker-behavior-settings-design.md §2): five <c>AppSettings</c> columns plus
/// <c>Series.TrackerPromptShown</c>. <c>Down</c> is a deliberate no-op, so - per the up-down-up
/// antipattern note - only the migrate-to-HEAD defaults and round-trip are asserted.
/// </summary>
public class AddTrackerBehaviorSettingsMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public AddTrackerBehaviorSettingsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_trackerbehavior_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumns_WithSpecDefaults_ThatRoundTrip()
    {
        int seriesId;
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();

            Assert.True(settings.TrackerAutoOpenLinkPanel);
            Assert.True(settings.TrackerUpdateAfterReading);
            Assert.Equal(TrackerAutoUpdateMode.Always, settings.TrackerUpdateOnMarkRead);
            Assert.False(settings.TrackerAutoSyncFromTrackers);
            Assert.True(settings.TrackerUseSourceMetadata);

            var series = new Series { Name = "S" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;
            Assert.False(series.TrackerPromptShown);

            settings.TrackerAutoOpenLinkPanel = false;
            settings.TrackerUpdateAfterReading = false;
            settings.TrackerUpdateOnMarkRead = TrackerAutoUpdateMode.Ask;
            settings.TrackerAutoSyncFromTrackers = true;
            settings.TrackerUseSourceMetadata = false;
            series.TrackerPromptShown = true;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.False(settings.TrackerAutoOpenLinkPanel);
            Assert.False(settings.TrackerUpdateAfterReading);
            Assert.Equal(TrackerAutoUpdateMode.Ask, settings.TrackerUpdateOnMarkRead);
            Assert.True(settings.TrackerAutoSyncFromTrackers);
            Assert.False(settings.TrackerUseSourceMetadata);
            Assert.True(context.Series.Find(seriesId)!.TrackerPromptShown);
        }
    }

    [Fact]
    public void ModelHasNoPendingChanges()
    {
        using var context = CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
