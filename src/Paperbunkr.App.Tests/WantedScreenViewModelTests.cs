using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

[Collection(nameof(AvaloniaTestCollection))]
public class WantedScreenViewModelTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 9, 19);
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_wanted_{Guid.NewGuid():N}.db");
    private readonly List<string> _copied = new();
    private readonly List<int> _opened = new();
    private int _searchNowCalls;
    private bool _settingsOpened;
    private readonly FakeComicVine _comicVine = new();

    public WantedScreenViewModelTests()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath};Foreign Keys=True").Options);

    private WantedScreenViewModel Create() => new(
        NewContext,
        _ => { _searchNowCalls++; return Task.CompletedTask; },
        _opened.Add,
        () => _settingsOpened = true,
        text => { _copied.Add(text); return Task.CompletedTask; },
        new GrabService(NewContext, _ => null, new ChannelEventPublisher()),
        _ => _comicVine,
        post: a => a(),
        today: () => Today);

    private sealed class FakeComicVine : IComicVineClient
    {
        public List<ComicVineVolume> Volumes { get; } = new();
        public List<ComicVineIssue> Issues { get; } = new();
        public Exception? Throw { get; set; }

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken)
        {
            if (Throw is not null) throw Throw;
            return Task.FromResult<IReadOnlyList<ComicVineVolume>>(Volumes.ToList());
        }

        public Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken) => Task.FromResult(Volumes.FirstOrDefault(v => v.Id == volumeId));

        public Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken)
        {
            if (Throw is not null) throw Throw;
            return Task.FromResult<IReadOnlyList<ComicVineIssue>>(Issues.ToList());
        }
    }

    /// <summary>A tracked "Spawn" volume linked to a local series, with due, upcoming and candidate-bearing wanted issues.</summary>
    private (int WatchedId, int SeriesId) Seed()
    {
        using var context = NewContext();
        var series = new Series { Name = "Spawn" };
        context.Series.Add(series);
        context.SaveChanges();
        var watched = WantedService.TrackVolume(context, new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null), series.Id, watchFutureReleases: false);
        WantedService.RefreshCatalog(context, watched, new[]
        {
            new ComicVineIssue(1, "261", null, Today.AddDays(-7), null, null, 100),
            new ComicVineIssue(2, "262", null, Today.AddDays(-1), null, null, 100),
            new ComicVineIssue(3, "263", null, Today.AddDays(9), null, null, 100),
            new ComicVineIssue(4, "264", null, Today.AddDays(30), null, null, 100),
        });
        var due = WantedService.Request(context, watched, context.CatalogIssues.Single(c => c.ComicVineIssueId == 1));
        WantedService.Request(context, watched, context.CatalogIssues.Single(c => c.ComicVineIssueId == 2));
        WantedService.Request(context, watched, context.CatalogIssues.Single(c => c.ComicVineIssueId == 3));
        context.ReleaseCandidates.AddRange(
            new ReleaseCandidate { WantedIssueId = due.Id, Title = "Spawn 261 (1992) cbr", DownloadUrl = "magnet:low", SizeBytes = 40L * 1024 * 1024, Seeders = 3, Indexer = "IdxA", Score = 5, FoundAt = Today },
            new ReleaseCandidate { WantedIssueId = due.Id, Title = "Spawn 261 (1992) cbz", DownloadUrl = "magnet:high", SizeBytes = 45L * 1024 * 1024, Seeders = 20, Indexer = "IdxB", Score = 45, FoundAt = Today },
            new ReleaseCandidate { WantedIssueId = due.Id, Title = "Spawn v1-6", DownloadUrl = "magnet:pack", SizeBytes = 2L * 1024 * 1024 * 1024, Seeders = 9, Score = -990, IsPack = true, FoundAt = Today });
        context.SaveChanges();
        return (watched.Id, series.Id);
    }

    [Fact]
    public void Refresh_SplitsDueFromUpcoming_AndCountsEachTab()
    {
        Seed();
        var vm = Create();

        vm.Refresh();

        Assert.Equal(new[] { "Spawn #261", "Spawn #262" }, vm.WantedRows.Select(r => r.Title));
        Assert.Equal("Spawn #263", Assert.Single(vm.UpcomingRows).Title);
        Assert.Contains("Arrives", vm.UpcomingRows[0].Subtitle);
        Assert.Equal("Upcoming", vm.UpcomingRows[0].StatusText);
        Assert.Equal(3, vm.CandidateCount);
        Assert.False(vm.HasNoWanted);
        Assert.False(vm.HasNoUpcoming);
    }

    [Fact]
    public void Refresh_ShowsCandidatesBestFirst_WithPacksFlagged()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        var group = Assert.Single(vm.CandidateGroups);
        Assert.Equal("Spawn #261", group.Title);
        Assert.Equal(new[] { "Spawn 261 (1992) cbz", "Spawn 261 (1992) cbr", "Spawn v1-6" }, group.Candidates.Select(c => c.Title));
        Assert.Equal("45 MB · 20 seeders · IdxB", group.Candidates[0].Detail);
        Assert.True(group.Candidates[2].IsPack);
        Assert.Equal("2.0 GB · 9 seeders", group.Candidates[2].Detail);
        Assert.Equal(3, vm.WantedRows[0].CandidateCount);
        Assert.True(vm.WantedRows[0].HasCandidates);
        Assert.False(vm.WantedRows[1].HasCandidates);
    }

    [Fact]
    public void Refresh_ListsTrackedSeries_WithMissingAndWantedCounts()
    {
        var (_, seriesId) = Seed();
        var vm = Create();
        vm.Refresh();

        var row = Assert.Single(vm.SeriesRows);
        Assert.Equal("Spawn", row.Name);
        Assert.Equal("Image · 1992", row.Subtitle);
        Assert.Equal(seriesId, row.SeriesId);
        Assert.Equal(1, row.MissingCount);                 // #264 is neither owned nor wanted
        Assert.Equal(3, row.WantedCount);
        Assert.Equal("1 missing · 3 wanted", row.CountsText);
        Assert.False(row.WatchFutureReleases);
    }

    [Fact]
    public void EmptyStates_ReportNothingToShow()
    {
        var vm = Create();
        vm.Refresh();

        Assert.True(vm.HasNoWanted);
        Assert.True(vm.HasNoUpcoming);
        Assert.True(vm.HasNoCandidates);
        Assert.True(vm.HasNoSeries);
        Assert.False(vm.IsEnabled);                        // so the empty state can point at Acquisition settings
    }

    [Fact]
    public void Tabs_SwitchTheActiveTab()
    {
        var vm = Create();
        Assert.True(vm.IsWantedTab);

        vm.GoCandidatesCommand.Execute(null);
        Assert.True(vm.IsCandidatesTab);
        Assert.False(vm.IsWantedTab);

        vm.GoSeriesCommand.Execute(null);
        Assert.True(vm.IsSeriesTab);
        vm.GoUpcomingCommand.Execute(null);
        Assert.True(vm.IsUpcomingTab);
        vm.GoWantedCommand.Execute(null);
        Assert.True(vm.IsWantedTab);
    }

    [Fact]
    public void Ignore_MarksTheIssueIgnored_AndDropsItFromWanted()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        vm.IgnoreCommand.Execute(vm.WantedRows[0]);

        Assert.Equal(new[] { "Spawn #262" }, vm.WantedRows.Select(r => r.Title));
        using var context = NewContext();
        Assert.Equal(WantedIssueStatus.Ignored, context.WantedIssues.Single(w => w.ComicVineIssueId == 1).Status);
    }

    [Fact]
    public void Remove_DeletesTheWant_SoTheIssueReturnsToMissing()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        vm.RemoveCommand.Execute(vm.UpcomingRows[0]);

        Assert.Empty(vm.UpcomingRows);
        using var context = NewContext();
        Assert.DoesNotContain(context.WantedIssues, w => w.ComicVineIssueId == 3);
        var watched = context.WatchedSeries.Single();
        Assert.Contains(WantedService.GetMissing(context, watched), m => m.ComicVineIssueId == 3);
    }

    [Fact]
    public async Task CopyLink_SendsTheDownloadUrlToTheClipboard()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        await vm.CopyLinkCommand.ExecuteAsync(vm.CandidateGroups[0].Candidates[0]);

        Assert.Equal(new[] { "magnet:high" }, _copied);
        Assert.True(vm.HasInfoStatus);
    }

    [Fact]
    public async Task SearchNow_RunsTheDaemon_ThenRefreshes_AndResetsTheBusyFlag()
    {
        var vm = Create();
        vm.Refresh();
        Seed();                                            // a candidate appears "during" the search
        Assert.True(vm.HasNoWanted);

        await vm.SearchNowCommand.ExecuteAsync(null);

        Assert.Equal(1, _searchNowCalls);
        Assert.False(vm.IsSearching);
        Assert.False(vm.HasNoWanted);                      // refreshed afterwards
    }

    [Fact]
    public void OpenSeries_NavigatesOnlyWhenALocalSeriesIsLinked()
    {
        var (_, seriesId) = Seed();
        using (var context = NewContext())
        {
            WantedService.TrackVolume(context, new ComicVineVolume(200, "Unlinked", null, 2000, 5, null), seriesId: null, watchFutureReleases: false);
        }

        var vm = Create();
        vm.Refresh();
        var linked = vm.SeriesRows.Single(r => r.Name == "Spawn");
        var unlinked = vm.SeriesRows.Single(r => r.Name == "Unlinked");

        vm.OpenSeriesCommand.Execute(unlinked);
        Assert.Empty(_opened);
        Assert.False(unlinked.CanOpen);

        vm.OpenSeriesCommand.Execute(linked);
        Assert.Equal(new[] { seriesId }, _opened);
    }

    [Fact]
    public void FollowToggle_PersistsTheChoice_ButLoadingNeverFiresIt()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        vm.Refresh();                                      // a reload must not flip anything

        using (var context = NewContext())
        {
            Assert.False(context.WatchedSeries.Single().WatchFutureReleases);
        }

        vm.SeriesRows[0].WatchFutureReleases = true;

        using var check = NewContext();
        Assert.True(check.WatchedSeries.Single().WatchFutureReleases);
        Assert.Contains("Following Spawn", vm.StatusMessage);
    }

    [Fact]
    public async Task SeriesSearch_NeedsAComicVineKey_ThenListsResults_MarkingTrackedOnes()
    {
        var vm = Create();
        vm.SeriesSearchText = "Spawn";
        await vm.SearchSeriesCommand.ExecuteAsync(null);
        Assert.True(vm.HasErrorStatus);
        Assert.Empty(vm.SearchResults);

        using (var context = NewContext())
        {
            CredentialStore.Set(context, "ComicVine", CredentialKind.ApiKey, "CV");
            WantedService.TrackVolume(context, new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null), null, false);
        }

        _comicVine.Volumes.Add(new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null));
        _comicVine.Volumes.Add(new ComicVineVolume(101, "Spawn: Origins", "Image", 2009, 12, null));
        await vm.SearchSeriesCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.SearchResults.Count);
        Assert.True(vm.SearchResults[0].IsTracked);
        Assert.False(vm.SearchResults[1].IsTracked);
        Assert.Equal("Spawn (1992)", vm.SearchResults[0].Title);
        Assert.Equal("Image · 300 issues", vm.SearchResults[0].Subtitle);
        Assert.True(vm.HasSearchResults);
    }

    [Fact]
    public async Task TrackVolume_CachesTheCatalog_LinksALocalSeriesByName_AndReportsWhatIsMissing()
    {
        using (var context = NewContext())
        {
            context.Series.Add(new Series { Name = "The Boys" });
            context.SaveChanges();
            CredentialStore.Set(context, "ComicVine", CredentialKind.ApiKey, "CV");
        }

        _comicVine.Issues.AddRange(new[]
        {
            new ComicVineIssue(1, "1", null, Today.AddDays(-100), null, null, 300),
            new ComicVineIssue(2, "2", null, Today.AddDays(-70), null, null, 300),
        });
        var vm = Create();
        var result = new VolumeResultViewModel { Volume = new ComicVineVolume(300, "Boys", "Dynamite", 2006, 72, null) };

        await vm.TrackVolumeCommand.ExecuteAsync(result);

        using var check = NewContext();
        var watched = check.WatchedSeries.Single();
        Assert.False(watched.WatchFutureReleases);         // tracking is not following
        Assert.NotNull(watched.SeriesId);                  // "Boys" matched the local "The Boys"
        Assert.Equal(2, check.CatalogIssues.Count());
        Assert.Equal("Boys", vm.SeriesRows.Single().Name);
        Assert.Contains("2 of 2 issues missing", vm.StatusMessage);
    }

    [Fact]
    public async Task ComicVineFailures_BecomeAStatusMessage_NotACrash()
    {
        using (var context = NewContext())
        {
            CredentialStore.Set(context, "ComicVine", CredentialKind.ApiKey, "CV");
        }

        _comicVine.Throw = new ComicVineException("ComicVine rejected the API key.", 100);
        var vm = Create();
        vm.SeriesSearchText = "Spawn";

        await vm.SearchSeriesCommand.ExecuteAsync(null);
        Assert.Equal("ComicVine rejected the API key.", vm.StatusMessage);
        Assert.True(vm.HasErrorStatus);

        await vm.TrackVolumeCommand.ExecuteAsync(new VolumeResultViewModel { Volume = new ComicVineVolume(1, "X", null, null, 0, null) });
        Assert.True(vm.HasErrorStatus);
        using var check = NewContext();
        Assert.Empty(check.WatchedSeries);                  // nothing half-tracked
    }

    [Fact]
    public void OpenAcquisitionSettings_Navigates()
    {
        var vm = Create();

        vm.OpenAcquisitionSettingsCommand.Execute(null);

        Assert.True(_settingsOpened);
    }

    /// <summary>Proves the screen's compiled XAML was woven (see CLAUDE.md, "adding a new Avalonia View").</summary>
    [Fact]
    public void TheScreenView_Constructs_AndBindsToTheViewModel()
    {
        TestAppBuilder.EnsureInitialized();
        Seed();
        var vm = Create();
        vm.Refresh();

        var view = new WantedScreen { DataContext = vm };

        Assert.NotNull(view.Content);
    }

    /// <summary>A big backfill (an arc, a long-running series) can produce thousands of wants; only the visible rows may be realized.</summary>
    [Fact]
    public void TheWantedAndUpcomingLists_VirtualizeLongLists()
    {
        TestAppBuilder.EnsureInitialized();
        var vm = Create();
        for (int i = 0; i < 3000; i++)
        {
            var row = new WantedRowViewModel { Id = i, Title = $"Series #{i}", StatusText = "Wanted" };
            vm.WantedRows.Add(row);
            vm.UpcomingRows.Add(row);
        }

        var view = new WantedScreen { DataContext = vm };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        window.GetLayoutManager()?.ExecuteLayoutPass();

        int RealizedRows() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<Avalonia.Controls.TextBlock>().Count(t => t.IsEffectivelyVisible && t.Text is { } x && x.StartsWith("Series #", StringComparison.Ordinal));

        Assert.InRange(RealizedRows(), 1, 100);        // 3000 wants, one screenful realized

        vm.GoUpcomingCommand.Execute(null);
        window.GetLayoutManager()?.ExecuteLayoutPass();
        Assert.InRange(RealizedRows(), 1, 100);
    }
}
