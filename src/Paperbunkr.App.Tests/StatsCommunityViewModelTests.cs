using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Insights "You vs Community" card's view-model wiring (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 2):
/// lazy load in the Top Lists group, the empty state, the disagreement rows and the fetch action. The numbers are covered by the resolver tests in Paperbunkr.Data.Tests.
/// </summary>
public class StatsCommunityViewModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public StatsCommunityViewModelTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_stats_community_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static StatsScreenViewModel NewStats() => new(_ => { }, null, () => Now) { RunSynchronously = true };

    private static int AddSeries(string name, float? rating, decimal? community = null, string? linkedId = null)
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, Rating = rating };
        ctx.Series.Add(series);
        ctx.SaveChanges();
        if (community is { } value)
        {
            ctx.ExternalRatings.Add(new ExternalRating { SeriesId = series.Id, Provider = ExternalMetadataProvider.AniList, Value = value, Scale = 100, RetrievedAt = Now });
        }

        if (linkedId is not null)
        {
            ctx.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = series.Id, Provider = ExternalMetadataProvider.AniList, ExternalId = linkedId });
        }

        ctx.SaveChanges();
        return series.Id;
    }

    private sealed class FakeProvider : IMetadataProvider
    {
        public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.AniList;

        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>(Array.Empty<MetadataSearchResult>());

        public Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalMediaMetadata?>(new ExternalMediaMetadata(externalId, "T", "u", null, null, null, null, Rating: 70));
    }

    [Fact]
    public void TheCard_IsEmptyBeforeTheTopListsGroupIsShown_AndExplainsWhenThereIsNoRating()
    {
        var vm = NewStats();

        Assert.False(vm.HasCommunityChart);
        Assert.True(vm.CommunityIsEmpty);
        Assert.Equal("You haven't rated any series yet.", vm.CommunityEmptyText);
    }

    [Fact]
    public void TopListsGroup_LoadsTheComparison_AndListsTheBiggestDisagreements()
    {
        AddSeries("High", 5f, community: 60);
        AddSeries("Low", 1f, community: 90);
        AddSeries("Agree", 4f, community: 80);
        var vm = NewStats();

        vm.SetTrendsGroupCommand.Execute(TrendsGroup.TopLists);

        Assert.True(vm.HasCommunityChart);
        Assert.Equal(3, vm.Community.ComparableSeries);
        Assert.Equal("High", Assert.Single(vm.CommunityHigherRows).Name);
        Assert.Equal("You 100 · Community 60 (+40)", vm.CommunityHigherRows[0].Text);
        Assert.Equal("Low", Assert.Single(vm.CommunityLowerRows).Name);
        Assert.Contains("3 of 3 rated series have community data", vm.CommunityCoverageText);
        Assert.Contains("ComicVine reports none", vm.CommunityCoverageText);
    }

    [Fact]
    public void FewerThanThreeComparableSeries_ShowTheEmptyState_WithTheCounts()
    {
        AddSeries("One", 4f, community: 70);
        AddSeries("Rated only", 3f);
        var vm = NewStats();

        vm.SetTrendsGroupCommand.Execute(TrendsGroup.TopLists);

        Assert.False(vm.HasCommunityChart);
        Assert.Contains("Only 1 of your 2 rated series", vm.CommunityEmptyText);
    }

    [Fact]
    public void OpenCommunitySeries_GoesToThatSeries()
    {
        int id = AddSeries("High", 5f, community: 60);
        AddSeries("B", 4f, community: 80);
        AddSeries("C", 3f, community: 40);
        int? opened = null;
        var vm = NewStats();
        vm.GoDetailForSeries = s => opened = s;
        vm.SetTrendsGroupCommand.Execute(TrendsGroup.TopLists);

        vm.OpenCommunitySeriesCommand.Execute(vm.CommunityHigherRows[0]);

        Assert.Equal(id, opened);
    }

    [Fact]
    public void TheFetchButton_CountsTheRatedLinkedSeriesStillMissingAScore()
    {
        AddSeries("Needs it", 4f, linkedId: "1");
        AddSeries("Has it", 4f, community: 80, linkedId: "2");
        var vm = NewStats();

        vm.SetTrendsGroupCommand.Execute(TrendsGroup.TopLists);

        Assert.Equal(1, vm.CommunityFetchCandidates);
        Assert.True(vm.CanFetchCommunity);
        Assert.Equal("Fetch community ratings (1)", vm.FetchCommunityLabel);
    }

    [Fact]
    public async Task Fetching_StoresTheScores_ThenReloadsTheCard()
    {
        AddSeries("A", 4f, community: 80);
        AddSeries("B", 3f, community: 50);
        AddSeries("Missing", 5f, linkedId: "9");
        var vm = NewStats();
        vm.CommunityProviderFor = _ => new FakeProvider();
        vm.SetTrendsGroupCommand.Execute(TrendsGroup.TopLists);
        Assert.False(vm.HasCommunityChart);

        await vm.FetchCommunityRatingsCommand.ExecuteAsync(null);

        Assert.True(vm.HasCommunityChart);
        Assert.Equal(3, vm.Community.ComparableSeries);
        Assert.Equal(0, vm.CommunityFetchCandidates);
        Assert.False(vm.CanFetchCommunity);
        Assert.Equal("Community ratings are up to date", vm.FetchCommunityLabel);
        Assert.False(vm.IsFetchingCommunity);
    }
}
