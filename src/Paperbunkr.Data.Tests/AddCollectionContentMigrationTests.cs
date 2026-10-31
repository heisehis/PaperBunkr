using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>The AddCollectionContent migration (Metadata Model Phase 7), run for real against SQLite rather than <c>EnsureCreated</c>.</summary>
public sealed class AddCollectionContentMigrationTests
{
    [Fact]
    public void Migrate_CreatesTheTable_AndAnIssuesRangesFollowItsDeletion()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_content_migration_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options;
            using (var context = new PaperbunkrDbContext(options))
            {
                context.Database.Migrate();
                var series = new Series { Name = "S" };
                series.Issues.Add(new Issue { Number = "1", FilePath = "C:\\x\\1.cbz" });
                context.Series.Add(series);
                context.SaveChanges();
                context.CollectionContents.Add(new CollectionContent
                {
                    IssueId = series.Issues.Single().Id,
                    FromNumber = "1",
                    ToNumber = "10",
                    FromKey = 1,
                    ToKey = 10,
                    Source = CollectionContentSource.MangaDexVolume,
                    CreatedAt = DateTime.UtcNow,
                });
                context.SaveChanges();
            }

            using (var read = new PaperbunkrDbContext(options))
            {
                var row = Assert.Single(read.CollectionContents);
                Assert.Equal(CollectionContentSource.MangaDexVolume, row.Source);
                Assert.Equal(10f, row.ToKey);
                read.Issues.RemoveRange(read.Issues);
                read.SaveChanges();
            }

            using var after = new PaperbunkrDbContext(options);
            Assert.Empty(after.CollectionContents);
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
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_content_pending_{Guid.NewGuid():N}.db");
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
