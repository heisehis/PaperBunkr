using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

/// <summary>AddCollectionBannerSlim (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md), run for real against SQLite.</summary>
public sealed class AddCollectionBannerSlimMigrationTests
{
    [Fact]
    public void Migrate_AddsTheSettingOff_AndTheModelHasNoPendingChanges()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_banner_slim_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options;
            using var context = new PaperbunkrDbContext(options);
            context.Database.Migrate();
            Assert.False(context.GetOrCreateAppSettings().CollectionBannerSlim);
            Assert.False(context.Database.HasPendingModelChanges());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
