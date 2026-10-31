using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>The community-ratings backfill behind the Insights "Fetch community ratings" action (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 2). No network.</summary>
public class CommunityRatingBackfillServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_backfill_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public CommunityRatingBackfillServiceTests()
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

    private PaperbunkrDbContext Open() => new(_options);

    private int Add(string name, float? rating, params (ExternalMetadataProvider Provider, string Id)[] links)
    {
        using var context = Open();
        var series = new Series { Name = name, Rating = rating };
        context.Series.Add(series);
        context.SaveChanges();
        foreach (var (provider, id) in links)
        {
            context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = series.Id, Provider = provider, ExternalId = id });
        }

        context.SaveChanges();
        return series.Id;
    }

    private static ExternalMediaMetadata Meta(string id, double? rating, int? votes = null) =>
        new(id, "T", "https://x/" + id, null, null, null, null, Rating: rating, RatingVoteCount: votes);

    private sealed class Fake : IMetadataProvider
    {
        public Func<string, ExternalMediaMetadata?> Get { get; set; } = _ => null;

        public List<string> Requested { get; } = new();

        public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.AniList;

        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>(Array.Empty<MetadataSearchResult>());

        public Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken)
        {
            Requested.Add(externalId);
            return Task.FromResult(Get(externalId));
        }
    }

    [Fact]
    public void Candidates_AreRatedSeriesWithALinkAndNoStoredRatingFromThatProvider()
    {
        Add("Rated, linked", 4f, (ExternalMetadataProvider.AniList, "1"));
        Add("Unrated", null, (ExternalMetadataProvider.AniList, "2"));
        Add("Rated, no link", 4f);
        int already = Add("Already has it", 3f, (ExternalMetadataProvider.AniList, "3"));
        Add("Tracker only", 5f, (ExternalMetadataProvider.MyAnimeList, "4"));
        using (var context = Open())
        {
            context.ExternalRatings.Add(new ExternalRating { SeriesId = already, Provider = ExternalMetadataProvider.AniList, Value = 80, Scale = 100, RetrievedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        using var read = Open();
        var candidate = Assert.Single(CommunityRatingBackfillService.Candidates(read));

        Assert.Equal("1", candidate.ExternalId);
    }

    [Fact]
    public void ARatedSeries_LinkedToTwoProviders_IsFetchedFromEachOneMissing()
    {
        int id = Add("Both", 4f, (ExternalMetadataProvider.AniList, "a"), (ExternalMetadataProvider.MangaDex, "m"));
        using (var context = Open())
        {
            context.ExternalRatings.Add(new ExternalRating { SeriesId = id, Provider = ExternalMetadataProvider.AniList, Value = 80, Scale = 100, RetrievedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        using var read = Open();
        var candidate = Assert.Single(CommunityRatingBackfillService.Candidates(read));

        Assert.Equal(ExternalMetadataProvider.MangaDex, candidate.Provider);
    }

    [Fact]
    public async Task Run_StoresTheScore_WithItsVoteCount_AndCountsSeriesTheProviderHasNoScoreFor()
    {
        int scored = Add("Scored", 4f, (ExternalMetadataProvider.AniList, "10"));
        Add("Unscored", 4f, (ExternalMetadataProvider.AniList, "11"));
        var fake = new Fake { Get = id => id == "10" ? Meta(id, 82.5, votes: 40) : Meta(id, null) };

        var result = await CommunityRatingBackfillService.RunAsync(Open, _ => fake);

        Assert.Equal(new CommunityBackfillResult(2, 1, 1, false), result);
        using var context = Open();
        var rating = Assert.Single(context.ExternalRatings);
        Assert.Equal(scored, rating.SeriesId);
        Assert.Equal(82.5m, rating.Value);
        Assert.Equal(100m, rating.Scale);
        Assert.Equal(40, rating.VoteCount);
    }

    [Fact]
    public async Task Run_StopsEarlyWhenTheProviderIsUnavailable_AndKeepsWhatItGot()
    {
        Add("First", 4f, (ExternalMetadataProvider.AniList, "1"));
        Add("Second", 4f, (ExternalMetadataProvider.AniList, "2"));
        Add("Third", 4f, (ExternalMetadataProvider.AniList, "3"));
        var fake = new Fake
        {
            Get = id => id == "2" ? throw new MetadataProviderUnavailableException() : Meta(id, 70),
        };

        var result = await CommunityRatingBackfillService.RunAsync(Open, _ => fake);

        Assert.True(result.StoppedEarly);
        Assert.Equal(1, result.Stored);
        Assert.Equal(new[] { "1", "2" }, fake.Requested);
        using var context = Open();
        Assert.Single(context.ExternalRatings);
    }

    [Fact]
    public async Task Run_SkipsALinkWhoseProviderTheAppCannotFetchFrom()
    {
        Add("Series", 4f, (ExternalMetadataProvider.AniList, "1"));

        var result = await CommunityRatingBackfillService.RunAsync(Open, _ => null);

        Assert.Equal(new CommunityBackfillResult(1, 0, 0, false), result);
    }

    [Fact]
    public async Task Run_Cancelled_ThrowsAndStopsFetching()
    {
        Add("Series", 4f, (ExternalMetadataProvider.AniList, "1"));
        var fake = new Fake { Get = id => Meta(id, 60) };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => CommunityRatingBackfillService.RunAsync(Open, _ => fake, null, cts.Token));

        Assert.Empty(fake.Requested);
    }

    [Fact]
    public async Task Run_WithNothingToDo_ReportsZero()
    {
        var result = await CommunityRatingBackfillService.RunAsync(Open, _ => new Fake());

        Assert.Equal(new CommunityBackfillResult(0, 0, 0, false), result);
    }
}
