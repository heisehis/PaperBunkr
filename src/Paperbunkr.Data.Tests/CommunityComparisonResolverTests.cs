using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>"You vs Community" data (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 2) against real SQLite.</summary>
public class CommunityComparisonResolverTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_community_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public CommunityComparisonResolverTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
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

    private int Add(string name, float? rating, Action<PaperbunkrDbContext, int>? extra = null)
    {
        using var context = new PaperbunkrDbContext(_options);
        var series = new Series { Name = name, Rating = rating };
        context.Series.Add(series);
        context.SaveChanges();
        extra?.Invoke(context, series.Id);
        context.SaveChanges();
        return series.Id;
    }

    private static void External(PaperbunkrDbContext context, int seriesId, ExternalMetadataProvider provider, decimal value, decimal scale = 100) =>
        context.ExternalRatings.Add(new ExternalRating { SeriesId = seriesId, Provider = provider, Value = value, Scale = scale, RetrievedAt = DateTime.UtcNow });

    private static void ComicIssue(PaperbunkrDbContext context, int seriesId, float rating, int? count = null) =>
        context.Issues.Add(new Issue { SeriesId = seriesId, Number = Guid.NewGuid().ToString("N")[..4], FilePath = $"C:\\x\\{Guid.NewGuid():N}.cbz", CommunityRating = rating, CommunityRatingCount = count });

    private CommunityComparison Build()
    {
        using var context = new PaperbunkrDbContext(_options);
        return CommunityComparisonResolver.Build(context);
    }

    [Fact]
    public void NoRatedSeries_IsEmpty()
    {
        Add("Unrated", null, (c, id) => External(c, id, ExternalMetadataProvider.AniList, 80));

        var result = Build();

        Assert.Equal(0, result.RatedSeries);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ARatedSeriesWithNoCommunityData_CountsAsRated_ButIsNotComparable()
    {
        Add("Lonely", 4f);

        var result = Build();

        Assert.Equal(1, result.RatedSeries);
        Assert.Equal(0, result.ComparableSeries);
    }

    [Fact]
    public void Manga_ComparesYourStarsAgainstTheMeanOfEveryProvider()
    {
        Add("Berserk", 5f, (c, id) =>
        {
            External(c, id, ExternalMetadataProvider.AniList, 80);
            External(c, id, ExternalMetadataProvider.MangaDex, 90);
        });

        var row = Assert.Single(Build().Rows);

        Assert.Equal(100, row.User);
        Assert.Equal(85, row.Community);
        Assert.Equal(15, row.Delta);
        Assert.Equal(2, row.SourceCount);
    }

    [Fact]
    public void AProviderWithAnotherScale_IsNormalizedTo100()
    {
        Add("Series", 3f, (c, id) => External(c, id, ExternalMetadataProvider.MyAnimeList, 7.5m, scale: 10));

        var row = Assert.Single(Build().Rows);

        Assert.Equal(60, row.User);
        Assert.Equal(75, row.Community);
        Assert.Equal(-15, row.Delta);
    }

    [Fact]
    public void Comics_UseTheIssuesMetronRating_WeightedByVoteCount()
    {
        Add("Saga", 4f, (c, id) =>
        {
            ComicIssue(c, id, 5f, count: 30);
            ComicIssue(c, id, 3f, count: 10);
        });

        var row = Assert.Single(Build().Rows);

        // (5*30 + 3*10) / 40 = 4.5 stars = 90
        Assert.Equal(90, row.Community);
        Assert.Equal(80, row.User);
        Assert.Equal(1, row.SourceCount);
    }

    [Fact]
    public void ComicsWithoutCounts_AreAPlainMean_LikeCeAverageCommunityRating()
    {
        Add("Saga", 4f, (c, id) =>
        {
            ComicIssue(c, id, 4f);
            ComicIssue(c, id, 2f);
        });

        Assert.Equal(60, Assert.Single(Build().Rows).Community);
    }

    [Fact]
    public void ZeroRatings_AreIgnored()
    {
        Add("Series", 4f, (c, id) =>
        {
            ComicIssue(c, id, 0f, count: 5);
            External(c, id, ExternalMetadataProvider.AniList, 0);
        });

        Assert.Empty(Build().Rows);
    }

    [Fact]
    public void TheBiggestDisagreements_AreRankedEachWay_AndCapped()
    {
        for (int i = 1; i <= 7; i++)
        {
            int n = i;
            Add($"High {n}", 5f, (c, id) => External(c, id, ExternalMetadataProvider.AniList, 100 - n * 5));
        }

        Add("Low", 1f, (c, id) => External(c, id, ExternalMetadataProvider.AniList, 90));
        Add("Agree", 4f, (c, id) => External(c, id, ExternalMetadataProvider.AniList, 80));

        var result = Build();

        Assert.Equal(9, result.ComparableSeries);
        Assert.Equal(CommunityComparisonResolver.TopCount, result.HigherThanCommunity.Count);
        Assert.Equal("High 7", result.HigherThanCommunity[0].SeriesName);
        Assert.Equal("Low", Assert.Single(result.LowerThanCommunity).SeriesName);
        Assert.DoesNotContain(result.HigherThanCommunity.Concat(result.LowerThanCommunity), r => r.SeriesName == "Agree");
    }

    [Fact]
    public void CommunityData_OfAnotherSeries_NeverLeaksIn()
    {
        Add("Mine", 4f);
        Add("Theirs", null, (c, id) => External(c, id, ExternalMetadataProvider.AniList, 70));

        var result = Build();

        Assert.Equal(1, result.RatedSeries);
        Assert.Empty(result.Rows);
    }
}
