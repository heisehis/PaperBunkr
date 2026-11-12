using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

/// <summary>AddCollectionBadges (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md), run for real against SQLite.</summary>
public sealed class AddCollectionBadgesMigrationTests
{
    [Fact]
    public void Migrate_AddsTheSettingOff_AndTheModelHasNoPendingChanges()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_badges_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options;
            using var context = new PaperbunkrDbContext(options);
            context.Database.Migrate();
            Assert.False(context.GetOrCreateAppSettings().ShowCollectionBadges);
            Assert.False(context.Database.HasPendingModelChanges());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
