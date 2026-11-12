using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>AddCollectionIdentity (docs/superpowers/specs/2026-11-06-collection-identity-design.md), run for real against SQLite.</summary>
public sealed class AddCollectionIdentityMigrationTests
{
    [Fact]
    public void Migrate_AddsTheColumnsWithTheirDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_identity_migration_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options;
            using (var context = new PaperbunkrDbContext(options))
            {
                context.Database.Migrate();
                context.Collections.Add(new Collection { Name = "Plain" });
                context.Collections.Add(new Collection
                {
                    Name = "Dressed", IconGlyph = "Rocket", CoverStyle = CollectionCoverStyle.Fan, IsPinned = true, CoverMemberItemId = 7,
                });
                context.GetOrCreateAppSettings();
                context.SaveChanges();
            }

            using var read = new PaperbunkrDbContext(options);
            var plain = read.Collections.Single(c => c.Name == "Plain");
            Assert.Null(plain.IconGlyph);
            Assert.Equal(CollectionCoverStyle.Auto, plain.CoverStyle);
            Assert.False(plain.IsPinned);
            Assert.Null(plain.CoverMemberItemId);

            var dressed = read.Collections.Single(c => c.Name == "Dressed");
            Assert.Equal("Rocket", dressed.IconGlyph);
            Assert.Equal(CollectionCoverStyle.Fan, dressed.CoverStyle);
            Assert.True(dressed.IsPinned);
            Assert.Equal(7, dressed.CoverMemberItemId);

            var settings = read.GetOrCreateAppSettings();
            Assert.False(settings.ShowCollectionProgress);
            Assert.Equal(CollectionRowDensity.Comfortable, settings.CollectionRowDensity);
            Assert.Equal(CollectionDotShape.Circle, settings.CollectionDotShape);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TheModelHasNoPendingChanges_AfterTheMigration()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_identity_pending_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options;
            using var context = new PaperbunkrDbContext(options);
            context.Database.Migrate();
            Assert.False(context.Database.HasPendingModelChanges());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
