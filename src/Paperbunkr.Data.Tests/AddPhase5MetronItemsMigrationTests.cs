using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddPhase5MetronItems</c> migration (docs/superpowers/specs/2026-09-23-metron-api-
/// utilization-design.md) applies cleanly and the new schema round-trips. A full down-migrate is
/// deliberately not tested here, same reasoning as <see cref="AddReadingGoalsMigrationTests"/>.
/// </summary>
public class AddPhase5MetronItemsMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public AddPhase5MetronItemsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_phase5_mig_{Guid.NewGuid():N}.db");
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
    public void FullMigrate_RoundTripsNewColumnsAndTables()
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

            var issue = new Issue { SeriesId = seriesId, Number = "1", CommunityRating = 4.2f, CommunityRatingCount = 12 };
            ctx.Issues.Add(issue);
            ctx.SaveChanges();
            issueId = issue.Id;

            ctx.PullListReleases.Add(new PullListRelease
            {
                Provider = ComicProvider.Metron,
                ExternalIssueId = 900,
                SeriesId = 10,
                SeriesName = "Test Series",
                IssueNumber = "1",
                StoreDate = new DateTime(2026, 10, 7),
                FocDate = new DateTime(2026, 9, 15),
                FetchedAt = DateTime.UtcNow,
            });

            ctx.SeriesAssociations.Add(new SeriesAssociation { SeriesId = seriesId, Provider = ComicProvider.Metron, ExternalSeriesId = "42", Name = "Related Series" });
            ctx.IssueVariantCovers.Add(new IssueVariantCover { IssueId = issueId, Name = "2nd Print", ImageUrl = "https://x/variant.jpg" });
            ctx.SaveChanges();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var issue = ctx.Issues.Single(i => i.Id == issueId);
            Assert.Equal(4.2f, issue.CommunityRating);
            Assert.Equal(12, issue.CommunityRatingCount);

            var release = ctx.PullListReleases.Single();
            Assert.Equal(new DateTime(2026, 9, 15), release.FocDate);

            var association = ctx.SeriesAssociations.Single();
            Assert.Equal(seriesId, association.SeriesId);
            Assert.Equal("Related Series", association.Name);

            var variant = ctx.IssueVariantCovers.Single();
            Assert.Equal(issueId, variant.IssueId);
            Assert.Equal("https://x/variant.jpg", variant.ImageUrl);
        }
    }
}
