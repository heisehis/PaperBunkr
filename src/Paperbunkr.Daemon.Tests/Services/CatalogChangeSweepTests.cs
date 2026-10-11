using Paperbunkr.Daemon.Services;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;

namespace Paperbunkr.Daemon.Tests.Services;

/// <summary>
/// The background refresh's "what changed since" sweep (docs/superpowers/specs/2026-10-05-metron-api-
/// efficiency-and-matching-design.md section 1). The cycle only asks whether a provider's client is an
/// <see cref="ISeriesChangeSource"/>, so a scripted client stands in for Metron here.
/// </summary>
public class CatalogChangeSweepTests : CycleTestBase
{
    private sealed class SweepingClient : IComicVineClient, ISeriesChangeSource
    {
        public Dictionary<int, DateTime>? Changed { get; set; } = new();
        public Exception? SweepThrows { get; set; }
        public List<DateTime> Sweeps { get; } = new();
        public List<int> IssueCalls { get; } = new();

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ComicVineVolume>>(Array.Empty<ComicVineVolume>());

        public Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken) => Task.FromResult<ComicVineVolume?>(null);

        public Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken)
        {
            IssueCalls.Add(volumeId);
            return Task.FromResult<IReadOnlyList<ComicVineIssue>>(new List<ComicVineIssue>());
        }

        public Task<IReadOnlyDictionary<int, DateTime>?> GetSeriesModifiedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken)
        {
            Sweeps.Add(sinceUtc);
            if (SweepThrows is not null) throw SweepThrows;
            return Task.FromResult<IReadOnlyDictionary<int, DateTime>?>(Changed);
        }
    }

    private readonly SweepingClient _client = new();

    private AcquisitionCycle Cycle() => new(NewContext, (_, _) => Indexer, _ => _client, Events, () => Now);

    /// <summary>A followed volume whose catalog was last confirmed and last really fetched the given number of hours ago (null = never).</summary>
    private void Follow(int volumeId, double? refreshedHoursAgo, double? fetchedHoursAgo)
    {
        using var context = NewContext();
        var watched = WantedService.TrackVolume(context, new ComicVineVolume(volumeId, $"Series {volumeId}", "Image", 1992, 300, null), seriesId: null, watchFutureReleases: true);
        watched.LastRefreshedAt = refreshedHoursAgo is double r ? Now.AddHours(-r) : null;
        watched.LastCatalogFetchAt = fetchedHoursAgo is double f ? Now.AddHours(-f) : null;
        context.SaveChanges();
    }

    private DateTime? RefreshedAt(int volumeId)
    {
        using var context = NewContext();
        return context.WatchedSeries.Single(w => w.ExternalVolumeId == volumeId).LastRefreshedAt;
    }

    [Fact]
    public async Task UnchangedSeries_AreNotFetched_AndAreMarkedCurrent()
    {
        Configure(comicVineKey: true);
        Follow(100, refreshedHoursAgo: 20, fetchedHoursAgo: 20);
        Follow(101, refreshedHoursAgo: 14, fetchedHoursAgo: 14);
        Follow(102, refreshedHoursAgo: 13, fetchedHoursAgo: 13);
        _client.Changed = new() { [101] = Now.AddHours(-2) };

        await Cycle().RunAsync(manual: false, CancellationToken.None);

        Assert.Equal(new[] { 101 }, _client.IssueCalls);
        Assert.Equal(Now.AddHours(-20) - AcquisitionCycle.SweepMargin, Assert.Single(_client.Sweeps));   // from the oldest of the three
        Assert.Equal(Now, RefreshedAt(100));
        Assert.Equal(Now, RefreshedAt(102));
    }

    [Fact]
    public async Task AChangeFromBeforeASeriesWasLastRefreshed_DoesNotCount()
    {
        Configure(comicVineKey: true);
        Follow(100, refreshedHoursAgo: 40, fetchedHoursAgo: 40);
        Follow(101, refreshedHoursAgo: 13, fetchedHoursAgo: 13);
        // 101 shows up because the sweep reaches back to 100's last refresh, but it changed before its own.
        _client.Changed = new() { [100] = Now.AddHours(-30), [101] = Now.AddHours(-30) };

        await Cycle().RunAsync(manual: false, CancellationToken.None);

        Assert.Equal(new[] { 100 }, _client.IssueCalls);
    }

    [Fact]
    public async Task ASeriesNotReallyFetchedForAWeek_IsFetchedWhateverTheSweepSays()
    {
        Configure(comicVineKey: true);
        Follow(100, refreshedHoursAgo: 13, fetchedHoursAgo: 8 * 24);
        Follow(101, refreshedHoursAgo: 13, fetchedHoursAgo: 13);
        Follow(102, refreshedHoursAgo: 13, fetchedHoursAgo: 13);

        await Cycle().RunAsync(manual: false, CancellationToken.None);

        Assert.Equal(new[] { 100 }, _client.IssueCalls);
    }

    [Fact]
    public async Task NeverFetchedSeries_AreAlwaysFetched_AndOneCandidateIsNotWorthASweep()
    {
        Configure(comicVineKey: true);
        Follow(100, refreshedHoursAgo: null, fetchedHoursAgo: null);
        Follow(101, refreshedHoursAgo: 13, fetchedHoursAgo: 13);

        await Cycle().RunAsync(manual: false, CancellationToken.None);

        Assert.Empty(_client.Sweeps);
        Assert.Equal(new[] { 100, 101 }, _client.IssueCalls.OrderBy(i => i));
    }

    [Fact]
    public async Task ASweepThatFails_OrCannotTell_FetchesEverything()
    {
        Configure(comicVineKey: true);
        Follow(100, refreshedHoursAgo: 13, fetchedHoursAgo: 13);
        Follow(101, refreshedHoursAgo: 13, fetchedHoursAgo: 13);
        _client.SweepThrows = new ComicVineException("Metron returned HTTP 502.");

        await Cycle().RunAsync(manual: false, CancellationToken.None);
        Assert.Equal(new[] { 100, 101 }, _client.IssueCalls.OrderBy(i => i));

        // Same again when the provider says too much changed to list.
        _client.IssueCalls.Clear();
        _client.SweepThrows = null;
        _client.Changed = null;
        using (var context = NewContext())
        {
            foreach (var watched in context.WatchedSeries)
            {
                watched.LastRefreshedAt = Now.AddHours(-13);
            }

            context.SaveChanges();
        }

        await Cycle().RunAsync(manual: false, CancellationToken.None);
        Assert.Equal(new[] { 100, 101 }, _client.IssueCalls.OrderBy(i => i));
    }

    [Fact]
    public async Task AClientThatCannotSweep_IsRefreshedExactlyAsBefore()
    {
        Configure(comicVineKey: true);
        Follow(100, refreshedHoursAgo: 13, fetchedHoursAgo: 13);
        Follow(101, refreshedHoursAgo: 13, fetchedHoursAgo: 13);

        await NewCycle().RunAsync(manual: false, CancellationToken.None);   // the base fixture's plain client

        Assert.Equal(new[] { 100, 101 }, ComicVine.IssueCalls.OrderBy(i => i));
    }
}
