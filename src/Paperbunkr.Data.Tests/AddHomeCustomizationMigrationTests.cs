using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddHomeCustomization</c> migration (docs/superpowers/specs/2026-09-28-home-improvements-design.md): the three
/// Home settings default to unset/off and round-trip, and the <c>DismissedRecommendations</c> table exists with its unique series
/// index. <c>Down</c> leaves the AppSettings columns in place by design, so no up-down-up is asserted.
/// </summary>
public class AddHomeCustomizationMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_home_customization_migration_test_{Guid.NewGuid():N}.db");

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
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_AddsSettings_UnsetByDefault()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var settings = context.GetOrCreateAppSettings();
        Assert.Null(settings.HomeSectionOrder);
        Assert.Null(settings.HomeHiddenSections);
        Assert.False(settings.HomeSeasonalFlourish);
    }

    [Fact]
    public void Settings_AndDismissals_RoundTrip()
    {
        int seriesId;
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            settings.HomeSectionOrder = "recentlyAdded,spotlight";
            settings.HomeHiddenSections = "collections";
            settings.HomeSeasonalFlourish = true;
            var series = new Series { Name = "S" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;
            context.DismissedRecommendations.Add(new DismissedRecommendation { SeriesId = seriesId, DismissedUtc = DateTime.UtcNow });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.Equal("recentlyAdded,spotlight", settings.HomeSectionOrder);
            Assert.Equal("collections", settings.HomeHiddenSections);
            Assert.True(settings.HomeSeasonalFlourish);
            Assert.Equal(seriesId, context.DismissedRecommendations.Single().SeriesId);

            context.DismissedRecommendations.Add(new DismissedRecommendation { SeriesId = seriesId, DismissedUtc = DateTime.UtcNow });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges()); // unique per series
        }
    }
}
