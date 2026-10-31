using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.SeriesConnector;
using Paperbunkr.Data.Tests.StoryEventIdentity;

namespace Paperbunkr.Data.Tests.SeriesConnector;

/// <summary><see cref="MetronAssociationFetch"/> against a fake Metron (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.1). No network.</summary>
public class MetronAssociationFetchTests : IdentityTestDb
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeMetron : IMetronSeriesLinks
    {
        public Dictionary<int, List<int>> ByComicVine { get; } = new();
        public Dictionary<int, List<int>> ByGcd { get; } = new();
        public Dictionary<int, List<ComicVineIdName>> Associated { get; } = new();
        public Dictionary<int, PullListSeriesInfo> Info { get; } = new();
        public HashSet<int> FailAssociatedFor { get; } = new();
        public List<string> Calls { get; } = new();

        public Task<IReadOnlyList<ComicVineIdName>> GetAssociatedSeriesAsync(int metronSeriesId, CancellationToken cancellationToken)
        {
            Calls.Add($"associated {metronSeriesId}");
            if (FailAssociatedFor.Contains(metronSeriesId))
            {
                throw new ComicVineException("boom");
            }

            return Task.FromResult<IReadOnlyList<ComicVineIdName>>(Associated.GetValueOrDefault(metronSeriesId) ?? new());
        }

        public Task<IReadOnlyList<ComicSeriesHit>> FindSeriesByComicVineIdAsync(int comicVineSeriesId, CancellationToken cancellationToken)
        {
            Calls.Add($"cv {comicVineSeriesId}");
            return Task.FromResult<IReadOnlyList<ComicSeriesHit>>((ByComicVine.GetValueOrDefault(comicVineSeriesId) ?? new()).Select(id => new ComicSeriesHit(id)).ToList());
        }

        public Task<IReadOnlyList<ComicSeriesHit>> FindSeriesByGcdIdAsync(int gcdSeriesId, CancellationToken cancellationToken)
        {
            Calls.Add($"gcd {gcdSeriesId}");
            return Task.FromResult<IReadOnlyList<ComicSeriesHit>>((ByGcd.GetValueOrDefault(gcdSeriesId) ?? new()).Select(id => new ComicSeriesHit(id)).ToList());
        }

        public Task<PullListSeriesInfo?> GetSeriesInfoAsync(int metronSeriesId, CancellationToken cancellationToken)
        {
            Calls.Add($"info {metronSeriesId}");
            return Task.FromResult(Info.GetValueOrDefault(metronSeriesId));
        }
    }

    private int AddSeries(string name, string? metronId = null, string? comicVineId = null, int? gcd = null, DateTime? checkedAt = null)
    {
        using var context = NewContext();
        var series = new Series { Name = name, GcdSeriesId = gcd, MetronAssociationsCheckedUtc = checkedAt };
        context.Series.Add(series);
        context.SaveChanges();
        if (metronId is not null)
        {
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = series.Id, Provider = ComicProvider.Metron, ExternalId = metronId });
        }

        if (comicVineId is not null)
        {
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = series.Id, Provider = ComicProvider.ComicVine, ExternalId = comicVineId });
        }

        context.SaveChanges();
        return series.Id;
    }

    private string? MetronIdOf(int seriesId)
    {
        using var context = NewContext();
        return context.ComicMetadataExternalIds.AsNoTracking()
            .SingleOrDefault(x => x.EntityKind == ComicMetadataEntityKind.Series && x.Provider == ComicProvider.Metron && x.EntityId == seriesId)?.ExternalId;
    }

    [Fact]
    public async Task ComicVineOnlySeries_GetsItsMetronId_FromASingleHit_ButNotFromTwo()
    {
        int one = AddSeries("Superman Unlimited", comicVineId: "555");
        int two = AddSeries("Ambiguous", comicVineId: "666");
        var metron = new FakeMetron();
        metron.ByComicVine[555] = new() { 9001 };
        metron.ByComicVine[666] = new() { 1, 2 };

        var result = await MetronAssociationFetch.RunAsync(Factory, metron, Now, CancellationToken.None);

        Assert.Equal(1, result.Resolved);
        Assert.Equal("9001", MetronIdOf(one));
        Assert.Null(MetronIdOf(two));
        Assert.Contains("associated 9001", metron.Calls); // resolved in pass 1, fetched in pass 2 of the same run
    }

    [Fact]
    public async Task GcdOnlySeries_IsResolvedThroughTheGcdFilter()
    {
        int series = AddSeries("Supergirl", gcd: 777);
        var metron = new FakeMetron();
        metron.ByGcd[777] = new() { 4242 };

        await MetronAssociationFetch.RunAsync(Factory, metron, Now, CancellationToken.None);

        Assert.Equal("4242", MetronIdOf(series));
    }

    [Fact]
    public async Task OnlyDueSeriesAreAsked_AndStampsAreSetOnSuccessNotOnFailure()
    {
        int fresh = AddSeries("Checked last week", metronId: "1", checkedAt: Now.AddDays(-7));
        int stale = AddSeries("Checked last year", metronId: "2", checkedAt: Now.AddDays(-400));
        int failing = AddSeries("Never checked", metronId: "3");
        var metron = new FakeMetron();
        metron.Associated[2] = new() { new ComicVineIdName(50, "Companion") };
        metron.FailAssociatedFor.Add(3);

        var result = await MetronAssociationFetch.RunAsync(Factory, metron, Now, CancellationToken.None);

        Assert.DoesNotContain("associated 1", metron.Calls);
        Assert.Equal((1, 1), (result.Checked, result.Failed));
        using var context = NewContext();
        Assert.Equal(Now, context.Series.Single(s => s.Id == stale).MetronAssociationsCheckedUtc);
        Assert.Null(context.Series.Single(s => s.Id == failing).MetronAssociationsCheckedUtc);
        Assert.Equal("Companion", context.SeriesAssociations.Single(a => a.SeriesId == stale).Name);
        Assert.Equal(Now.AddDays(-7), context.Series.Single(s => s.Id == fresh).MetronAssociationsCheckedUtc);
    }

    [Fact]
    public async Task UnmatchedAssociations_GetTheirComicVineAndGcdIds_OncePerMetronSeries_AndKeepThemOnReSync()
    {
        int a = AddSeries("Superman", metronId: "10");
        int b = AddSeries("Action Comics", metronId: "11");
        var metron = new FakeMetron();
        metron.Associated[10] = new() { new ComicVineIdName(99, "Superman Unlimited") };
        metron.Associated[11] = new() { new ComicVineIdName(99, "Superman Unlimited") };
        metron.Info[99] = new PullListSeriesInfo(99, "Superman Unlimited", "DC Comics", 2025, ComicVineId: 555, GcdId: 777);

        var result = await MetronAssociationFetch.RunAsync(Factory, metron, Now, CancellationToken.None);

        Assert.Equal(2, result.IdsFilled);
        Assert.Single(metron.Calls, c => c == "info 99");
        using (var context = NewContext())
        {
            Assert.All(context.SeriesAssociations.ToList(), r => Assert.Equal((555, 777), (r.ComicVineSeriesId!.Value, r.GcdSeriesId!.Value)));
        }

        // A later run: everything is fresh, so nothing is asked again, and the ids stay.
        metron.Calls.Clear();
        await MetronAssociationFetch.RunAsync(Factory, metron, Now.AddDays(1), CancellationToken.None);
        Assert.Empty(metron.Calls);
        using (var context = NewContext())
        {
            Assert.All(context.SeriesAssociations.ToList(), r => Assert.Equal(555, r.ComicVineSeriesId));
        }
    }

    [Fact]
    public async Task Cancellation_StopsTheRun()
    {
        AddSeries("Superman", metronId: "10");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MetronAssociationFetch.RunAsync(Factory, new FakeMetron(), Now, cts.Token));
    }
}
