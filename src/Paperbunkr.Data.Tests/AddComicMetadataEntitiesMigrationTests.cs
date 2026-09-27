using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddComicMetadataEntities</c> migration (docs/superpowers/specs/2026-09-23-metron-
/// api-utilization-design.md) applies cleanly on a full migrate and that the new schema round-trips.
/// A full down-migrate is deliberately not tested here, same reasoning as
/// <see cref="AddReadingGoalsMigrationTests"/>/<see cref="AddLibrarySnapshotsMigrationTests"/>.
/// </summary>
public class AddComicMetadataEntitiesMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public AddComicMetadataEntitiesMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_comicmetadata_mig_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
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

    [Fact]
    public void FullMigrate_CreatesNewTables_AndRoundTripsRows()
    {
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Database.Migrate();
        }

        int seriesId, issueId;
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Test Series" };
            ctx.Series.Add(series);
            ctx.SaveChanges();
            seriesId = series.Id;

            var issue = new Issue { SeriesId = seriesId, Number = "1" };
            ctx.Issues.Add(issue);
            ctx.SaveChanges();
            issueId = issue.Id;
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var team = new Team { Name = "Avengers" };
            var location = new Location { Name = "Gotham City" };
            var creator = new Creator { Name = "Stan Lee" };
            var publisher = new Publisher { Name = "Marvel" };
            ctx.Teams.Add(team);
            ctx.Locations.Add(location);
            ctx.Creators.Add(creator);
            ctx.Publishers.Add(publisher);
            ctx.SaveChanges();

            ctx.TeamAppearances.Add(new TeamAppearance { TeamId = team.Id, IssueId = issueId });
            ctx.LocationAppearances.Add(new LocationAppearance { LocationId = location.Id, IssueId = issueId });
            ctx.CreatorCredits.Add(new CreatorCredit { CreatorId = creator.Id, IssueId = issueId, Role = "Writer" });
            ctx.CreatorCredits.Add(new CreatorCredit { CreatorId = creator.Id, IssueId = issueId, Role = "Penciller" });
            ctx.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
            {
                EntityKind = ComicMetadataEntityKind.Creator,
                EntityId = creator.Id,
                Provider = ComicProvider.Metron,
                ExternalId = "42",
            });

            var series = ctx.Series.Single(s => s.Id == seriesId);
            series.PublisherEntityId = publisher.Id;
            var issue = ctx.Issues.Single(i => i.Id == issueId);
            issue.PublisherEntityId = publisher.Id;
            issue.Upc = "012345678905";
            ctx.SaveChanges();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.Equal("Avengers", ctx.Teams.Include(t => t.Appearances).Single().Name);
            Assert.Single(ctx.Teams.Single().Appearances);

            Assert.Equal("Gotham City", ctx.Locations.Include(l => l.Appearances).Single().Name);

            var creator = ctx.Creators.Include(c => c.Credits).Single();
            Assert.Equal("Stan Lee", creator.Name);
            Assert.Equal(2, creator.Credits.Count);
            Assert.Contains(creator.Credits, c => c.Role == "Writer");
            Assert.Contains(creator.Credits, c => c.Role == "Penciller");

            var publisher = ctx.Publishers.Single();
            Assert.Equal("Marvel", publisher.Name);
            Assert.Equal(publisher.Id, ctx.Series.Single().PublisherEntityId);
            Assert.Equal(publisher.Id, ctx.Issues.Single().PublisherEntityId);
            Assert.Equal("012345678905", ctx.Issues.Single().Upc);

            var externalId = ctx.ComicMetadataExternalIds.Single();
            Assert.Equal(ComicMetadataEntityKind.Creator, externalId.EntityKind);
            Assert.Equal(creator.Id, externalId.EntityId);
            Assert.Equal(ComicProvider.Metron, externalId.Provider);
            Assert.Equal("42", externalId.ExternalId);
        }
    }
}
