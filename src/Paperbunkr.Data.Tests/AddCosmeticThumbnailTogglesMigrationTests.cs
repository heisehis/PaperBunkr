using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddCosmeticThumbnailToggles</c> migration (docs/superpowers/specs/2026-09-13-
/// preferences-cosmetic-toggles-design.md) - 5 plain <c>AppSettings</c> bool columns, real
/// <c>DropColumn</c> per column on <c>Down()</c>, matching <c>AddIssueAlternateCount</c>'s current
/// convention. Rolls back only one step, same "unrelated pre-existing multi-step rollback bug"
/// caveat that migration's own test documents.
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

            var columns = context.Database
                .SqlQueryRaw<string>(
                    "SELECT name FROM pragma_table_info('AppSettings') WHERE name IN " +
                    "('FadeInThumbnails','DogEarThumbnails','ShowToolTips','NumericRatingThumbnails','ExportedListsContainFilenames');")
                .ToList();
            Assert.Empty(columns);
        }
    }
}
