using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddCosmeticThumbnailToggles</c> migration (docs/superpowers/specs/2026-09-13-
/// preferences-cosmetic-toggles-design.md) - 5 plain <c>AppSettings</c> bool columns. <c>Down()</c>
/// is a no-op (changed 2026-09-17 from a real per-column <c>DropColumn</c>): unlike
/// <c>AddIssueAlternateCount</c>'s <c>Issues</c>-table column, a <c>DropColumn</c> on
/// <c>AppSettings</c> triggers SQLite's full-table-rebuild path, which silently drops the
/// <c>LibraryGroupField</c>/<c>LibrarySortField</c>/<c>LibrarySortDirection</c> orphans left unmapped
/// since <c>UnifyLibrarySortGroupFields</c> - breaking any earlier <c>Down()</c> step whose own
/// rebuild target predates that migration. Same established no-op pattern as
/// <c>AddNavRailHoverExpandEnabled</c>; these 5 columns are expected to persist as harmless orphans
/// on down-migrate.
/// </summary>
public class AddCosmeticThumbnailTogglesMigrationTests : IDisposable
{
    private const string PriorMigration = "20260912122257_AddIssueAlternateCount";
    private readonly string _dbPath;

    public AddCosmeticThumbnailTogglesMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cosmetictoggles_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumns_WithCeMatchingDefaults_ThatRoundTrip_AndAreReversible()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var settings = context.GetOrCreateAppSettings();

            // Defaults match CE exactly (design doc §2 decision 7).
            Assert.True(settings.FadeInThumbnails);
            Assert.True(settings.DogEarThumbnails);
            Assert.False(settings.ShowToolTips);
            Assert.True(settings.NumericRatingThumbnails);
            Assert.False(settings.ExportedListsContainFilenames);

            settings.FadeInThumbnails = false;
            settings.ShowToolTips = true;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.False(settings.FadeInThumbnails);
            Assert.True(settings.ShowToolTips);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            // Down() is a deliberate no-op (see class doc) - the 5 columns persist as harmless
            // orphans rather than being dropped, to avoid SQLite's full-table-rebuild silently
            // destroying the unmapped LibraryGroupField/LibrarySortField/LibrarySortDirection columns.
            var columns = context.Database
                .SqlQueryRaw<string>(
                    "SELECT name FROM pragma_table_info('AppSettings') WHERE name IN " +
                    "('FadeInThumbnails','DogEarThumbnails','ShowToolTips','NumericRatingThumbnails','ExportedListsContainFilenames');")
                .ToList();
            Assert.Equal(5, columns.Count);
        }
    }
}
