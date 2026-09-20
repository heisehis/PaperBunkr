using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Tests.Services;

/// <summary>The weekly pull list inside the acquisition cycle (docs/superpowers/specs/2026-09-20-weekly-pull-list-design.md).</summary>
public class PullListCycleTests : CycleTestBase
{
    private sealed class FakeSource : IPullListSource
    {
        public List<PullListEntry> Entries { get; } = new();
        public int Fetches { get; private set; }
        public ComicVineException? Throw { get; set; }

        public Task<IReadOnlyList<PullListEntry>> GetReleasesAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
        {
            Fetches++;
            if (Throw is not null) throw Throw;
            return Task.FromResult<IReadOnlyList<PullListEntry>>(Entries.ToList());
        }

        public Task<PullListSeriesInfo?> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken) =>
            Task.FromResult<PullListSeriesInfo?>(new PullListSeriesInfo(seriesId, "Spawn", "Image", 1992, null));
    }

    private void SaveMetronLogin()
    {
        using var context = NewContext();
        CredentialStore.Set(context, "Metron", CredentialKind.Username, "reader");
        CredentialStore.Set(context, "Metron", CredentialKind.Password, "pw");
    }

    private void FollowMetronSpawn()
    {
        using var context = NewContext();
        WantedService.TrackVolume(context, new ComicVineVolume(10, "Spawn", "Image", 1992, 300, null), seriesId: null, watchFutureReleases: true, ComicProvider.Metron);
    }

    private AcquisitionCycle CycleWith(FakeSource source) =>
        new(NewContext, (_, _) => Indexer, _ => ComicVine, Events, () => Now, createPullListSource: (_, _) => source);

    [Fact]
    public async Task TheCycleFetchesTheWeeklyList_AndMakesUpcomingWantsForFollowedSeries()
    {
        Configure();
        SaveMetronLogin();
        FollowMetronSpawn();
        var source = new FakeSource();
        source.Entries.Add(new PullListEntry(900, 10, "Spawn", "350", Now.Date.AddDays(7), null, null));
        source.Entries.Add(new PullListEntry(901, 77, "Batman", "1", Now.Date.AddDays(7), null, null));

        await CycleWith(source).RunAsync(manual: false, CancellationToken.None);

        using var context = NewContext();
        Assert.Equal(1, source.Fetches);
        Assert.Equal(2, context.PullListReleases.Count());
        var wanted = Assert.Single(context.WantedIssues);
        Assert.Equal("350", wanted.IssueNumber);
        Assert.Equal(1, WantedService.Upcoming(context, Now).Count());
    }

    [Fact]
    public async Task TheListIsFetchedAboutTwiceADay_NotOnEveryCycle_UnlessManualAnHourLater()
    {
        Configure();
        SaveMetronLogin();
        var source = new FakeSource();
        var cycle = CycleWith(source);

        await cycle.RunAsync(manual: false, CancellationToken.None);
        await cycle.RunAsync(manual: false, CancellationToken.None);
        Assert.Equal(1, source.Fetches);

        using (var context = NewContext())
        {
            context.GetOrCreateAcquisitionSettings().PullListRefreshedAt = Now.AddHours(-2);
            context.SaveChanges();
        }

        await cycle.RunAsync(manual: false, CancellationToken.None);
        Assert.Equal(1, source.Fetches);                 // scheduled: 12 hours
        await cycle.RunAsync(manual: true, CancellationToken.None);
        Assert.Equal(2, source.Fetches);                 // "Search now": an hour is enough
    }

    [Fact]
    public async Task WithoutAMetronLogin_NothingIsFetched_AndTheCycleCarriesOn()
    {
        Configure();
        var source = new FakeSource();

        await CycleWith(source).RunAsync(manual: true, CancellationToken.None);

        Assert.Equal(0, source.Fetches);
        Assert.Contains(Drain(), e => e is CycleCompletedEvent);
    }

    [Fact]
    public async Task ARejectedLoginRaisesTheMetronAlert_WithoutStoppingTheCycle()
    {
        Configure();
        SaveMetronLogin();
        var source = new FakeSource { Throw = new ComicVineException("Metron rejected your login.", 100) };

        await CycleWith(source).RunAsync(manual: true, CancellationToken.None);

        var events = Drain();
        Assert.Contains(events, e => e is DaemonAlertEvent alert && alert.Key == AcquisitionCycle.MetronAlert);
        Assert.Contains(events, e => e is CycleCompletedEvent);
    }

    [Fact]
    public async Task NewWantsFromTheList_AreAnnouncedOnce_AsOneBatch_AndNotAgainNextTime()
    {
        Configure();
        SaveMetronLogin();
        FollowMetronSpawn();
        var source = new FakeSource();
        for (int i = 0; i < 6; i++)
        {
            source.Entries.Add(new PullListEntry(900 + i, 10, "Spawn", (350 + i).ToString(), Now.Date.AddDays(7 + i), null, null));
        }

        var cycle = CycleWith(source);
        await cycle.RunAsync(manual: true, CancellationToken.None);

        var announced = Assert.Single(Drain().OfType<NewReleasesEvent>());
        Assert.Equal(6, announced.Count);
        Assert.Equal(4, announced.Labels.Count);                            // the message names a few, the count says the rest
        Assert.Equal("Spawn #350", announced.Labels[0]);

        await cycle.RunAsync(manual: true, CancellationToken.None);
        Assert.Empty(Drain().OfType<NewReleasesEvent>());                   // nothing new, nothing said
    }
}
