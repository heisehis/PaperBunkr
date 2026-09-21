using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The weekly list became source-aware: <c>PullListReleases</c> gained <c>Provider</c>, and <c>MetronSeries</c> became <c>ReleaseSeries</c> keyed by (Provider, SeriesId). Both were
/// Metron's alone until now, so what is cached must survive as Metron's (docs/superpowers/specs/2026-09-20-weekly-pull-list-design.md).
/// </summary>
public class AddReleaseListProviderMigrationTests : IDisposable
{
    private const string PriorMigration = "20260920155556_AddPullListReleaseHidden";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_releaselist_migration_test_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    [Fact]
    public void ExistingCacheRowsBecomeMetrons_AndTheMigrationReverses()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw(
                "INSERT INTO PullListReleases (ExternalIssueId, SeriesId, SeriesName, IssueNumber, StoreDate, FetchedAt, IsHidden) VALUES (900, 10, 'Spawn', '350', '2026-09-30', '2026-09-20', 1)");
            context.Database.ExecuteSqlRaw(
                "INSERT INTO MetronSeries (SeriesId, Name, Publisher, YearBegan, ComicVineId, FetchedAt) VALUES (10, 'Spawn', 'Image', 1992, 4321, '2026-09-20')");
        }

        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var release = context.PullListReleases.Single();
            Assert.Equal(Paperbunkr.Data.Entities.ComicProvider.Metron, release.Provider);
            Assert.True(release.IsHidden);                                                    // the user's choice is kept
            var info = context.ReleaseSeries.Single();
            Assert.Equal((Paperbunkr.Data.Entities.ComicProvider.Metron, 10, "Image", 4321), (info.Provider, info.SeriesId, info.Publisher, info.ComicVineId));
            Assert.Empty(context.Database.SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'MetronSeries'").ToList());

            // the same numeric ids under ComicVine are separate rows now
            context.ReleaseSeries.Add(new Paperbunkr.Data.Entities.ReleaseSeriesInfo { Provider = Paperbunkr.Data.Entities.ComicProvider.ComicVine, SeriesId = 10, Name = "Other", FetchedAt = DateTime.UtcNow });
            context.SaveChanges();
            Assert.Equal(2, context.ReleaseSeries.Count());
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            Assert.Equal(1L, context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM MetronSeries").Single());        // only Metron's row goes back
            Assert.Equal(1L, context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM PullListReleases WHERE ExternalIssueId = 900").Single());
        }
    }
}
