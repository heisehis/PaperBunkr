using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddCosmeticsPitchSettings</c> migration (docs/superpowers/specs/2026-09-21-
/// cosmetics-pitch-design.md) - <c>BindingSpine</c>, <c>ProgressRing</c>, <c>GlowTier</c>, <c>ReadingListMosaic</c>, <c>SplashAmbientMotion</c> (and, from the follow-up <c>AddShowSelectionCheckbox</c>, <c>ShowSelectionCheckbox</c>). <c>Down()</c>
/// is a deliberate no-op (same AppSettings pattern as <c>AddCosmeticThumbnailToggles</c>), so the
/// columns persist as orphans on down-migrate.
/// </summary>
public class AddCosmeticsPitchSettingsMigrationTests : IDisposable
{
    private const string PriorMigration = "20260921003753_AddReleaseListProvider";
    private readonly string _dbPath;

    public AddCosmeticsPitchSettingsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cosmeticspitch_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumns_WithExpectedDefaults_ThatRoundTrip_AndPersistOnDownMigrate()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var settings = context.GetOrCreateAppSettings();
            Assert.True(settings.BindingSpine);
            Assert.True(settings.ProgressRing);
            Assert.Equal(2, settings.GlowTier); // Normal = today's look.
            Assert.True(settings.ReadingListMosaic);
            Assert.True(settings.SplashAmbientMotion);
            Assert.False(settings.ShowSelectionCheckbox); // multi-select is Ctrl/Shift+click by default
            Assert.True(settings.HeroBackdrop);           // the backdrop predates the switch, so on by default
            Assert.False(settings.SeriesAccentColor);     // opt-in: changes the look per screen
            Assert.Equal(1, settings.DensityPreset);      // Comfortable = the spacing the app always had

            settings.BindingSpine = false;
            settings.GlowTier = 3;
            settings.ReadingListMosaic = false;
            settings.ShowSelectionCheckbox = true;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.False(settings.BindingSpine);
            Assert.True(settings.ProgressRing);
            Assert.Equal(3, settings.GlowTier);
            Assert.False(settings.ReadingListMosaic);
            Assert.True(settings.SplashAmbientMotion);
            Assert.True(settings.ShowSelectionCheckbox);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var columns = context.Database
                .SqlQueryRaw<string>(
                    "SELECT name FROM pragma_table_info('AppSettings') WHERE name IN " +
                    "('BindingSpine','ProgressRing','GlowTier','ReadingListMosaic','SplashAmbientMotion','ShowSelectionCheckbox','HeroBackdrop','SeriesAccentColor','DensityPreset');")
                .ToList();
            Assert.Equal(9, columns.Count);
        }
    }
}
