using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Tracking;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="TrackerLinkResolver"/> (docs/superpowers/specs/2026-08-23-tracker-write-
/// back-sync-design.md) against a fake <see cref="ITrackerSearchProvider"/> - no real network
/// involved, same shape as <see cref="MetadataLinkResolverTests"/>. The one thing these tests exist
/// to prove that <c>MetadataLinkResolverTests</c> doesn't need to: <see cref="TrackerLinkResolver.Link"/>
/// touches only <see cref="TrackingLink"/>, never <see cref="ExternalMediaId"/>/<see cref="SeriesTitle"/>/
/// <see cref="ExternalMetadataSnapshot"/> - the tracker-vs-scraper boundary this feature's design
/// spec calls out as load-bearing.
/// </summary>
public class TrackerLinkResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public TrackerLinkResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_tracker_link_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
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

    private static int SeedSeries(PaperbunkrDbContext context, string name)
    {
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private sealed class FakeSearchProvider : ITrackerSearchProvider
    {
        public TrackingService Service => TrackingService.MyAnimeList;

        public List<MetadataSearchResult> SearchResults { get; } = new();

        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>(SearchResults);
    }

    [Fact]
    public async Task SearchAsync_ScoresAndOrdersByConfidenceDescending()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        var provider = new FakeSearchProvider();
        provider.SearchResults.Add(new MetadataSearchResult("2", "Naruto", "https://example/2"));
        provider.SearchResults.Add(new MetadataSearchResult("1", "One Piece", "https://example/1"));

        var matches = await TrackerLinkResolver.SearchAsync(provider, context, seriesId, "one piece", CancellationToken.None);

        Assert.Equal(2, matches.Count);
        Assert.Equal("1", matches[0].Result.ExternalId);
    }

    [Fact]
    public async Task SearchAsync_NonexistentSeries_ReturnsEmpty()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var provider = new FakeSearchProvider();
        provider.SearchResults.Add(new MetadataSearchResult("1", "One Piece", "https://example/1"));

        var matches = await TrackerLinkResolver.SearchAsync(provider, context, seriesId: 9999, "one piece", CancellationToken.None);

        Assert.Empty(matches);
    }

    [Fact]
    public void Link_NewLink_CreatesTrackingLink_AndNothingElse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");

        TrackerLinkResolver.Link(context, seriesId, TrackingService.MyAnimeList, "13");

        var link = Assert.Single(context.TrackingLinks);
        Assert.Equal(seriesId, link.SeriesId);
        Assert.Equal(TrackingService.MyAnimeList, link.Service);
        Assert.Equal("13", link.ExternalId);

        Assert.Empty(context.ExternalMediaIds);
        Assert.Empty(context.SeriesTitles);
        Assert.Empty(context.ExternalMetadataSnapshots);
    }

    [Fact]
    public void Link_ExistingLinkSameService_ReplacesExternalId_DoesNotDuplicate()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        TrackerLinkResolver.Link(context, seriesId, TrackingService.MyAnimeList, "13");

        TrackerLinkResolver.Link(context, seriesId, TrackingService.MyAnimeList, "999");

        var link = Assert.Single(context.TrackingLinks);
        Assert.Equal("999", link.ExternalId);
    }

    [Fact]
    public void Link_DifferentServicesSameSeries_CreatesSeparateLinks()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");

        TrackerLinkResolver.Link(context, seriesId, TrackingService.MyAnimeList, "13");
        TrackerLinkResolver.Link(context, seriesId, TrackingService.AniList, "30013");

        Assert.Equal(2, context.TrackingLinks.Count());
    }

    [Fact]
    public void Link_NonexistentSeries_DoesNothing()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);

        TrackerLinkResolver.Link(context, seriesId: 9999, TrackingService.MyAnimeList, "13");

        Assert.Empty(context.TrackingLinks);
    }

    [Fact]
    public void Unlink_RemovesOnlyTheMatchingServiceLink()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "One Piece");
        TrackerLinkResolver.Link(context, seriesId, TrackingService.MyAnimeList, "13");
        TrackerLinkResolver.Link(context, seriesId, TrackingService.AniList, "30013");

        TrackerLinkResolver.Unlink(context, seriesId, TrackingService.MyAnimeList);

        var remaining = Assert.Single(context.TrackingLinks);
        Assert.Equal(TrackingService.AniList, remaining.Service);
    }
}
