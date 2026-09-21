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
    private readonly List<(string Message, bool IsError)> _toasts = new();

    private static IEnumerable<QueueIssueViewModel> Issues(WantedScreenViewModel vm) => vm.QueueItems.OfType<QueueIssueViewModel>();

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
        today: () => Today,
        notify: (message, isError) => _toasts.Add((message, isError)));

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
        var due = WantedService.Request(context, watched, context.CatalogIssues.Single(c => c.ExternalIssueId == 1));
        WantedService.Request(context, watched, context.CatalogIssues.Single(c => c.ExternalIssueId == 2));
        WantedService.Request(context, watched, context.CatalogIssues.Single(c => c.ExternalIssueId == 3));
        context.ReleaseCandidates.AddRange(
            new ReleaseCandidate { WantedIssueId = due.Id, Title = "Spawn 261 (1992) cbr", DownloadUrl = "magnet:low", SizeBytes = 40L * 1024 * 1024, Seeders = 3, Indexer = "IdxA", Score = 5, FoundAt = Today },
            new ReleaseCandidate { WantedIssueId = due.Id, Title = "Spawn 261 (1992) cbz", DownloadUrl = "magnet:high", SizeBytes = 45L * 1024 * 1024, Seeders = 20, Indexer = "IdxB", Score = 45, FoundAt = Today },
            new ReleaseCandidate { WantedIssueId = due.Id, Title = "Spawn v1-6", DownloadUrl = "magnet:pack", SizeBytes = 2L * 1024 * 1024 * 1024, Seeders = 9, Score = -990, IsPack = true, FoundAt = Today });
        context.SaveChanges();
        return (watched.Id, series.Id);
    }

    [Fact]
    public void Refresh_ListsDueAndUpcomingIssuesInOneQueue_AndCountsEachStage()
    {
        Seed();
        var vm = Create();

        vm.Refresh();

        // The series needs the user (candidates to review), so its group opens on its own.
        Assert.Equal(new[] { "Spawn #261", "Spawn #262", "Spawn #263" }, Issues(vm).Select(r => r.Title));
        var upcoming = Issues(vm).Single(r => r.Stage == QueueStage.Upcoming);
        Assert.Equal("Spawn #263", upcoming.Title);
        Assert.Contains("Arrives", upcoming.Subtitle);
        Assert.Equal("Upcoming", upcoming.StatusText);
        Assert.Equal(3, vm.QueueCount);
        Assert.Equal(3, Issues(vm).Single(r => r.Title == "Spawn #261").CandidateCount);
        Assert.Equal(new[] { 3, 2, 1, 1, 0, 0, 0 }, vm.QueueChips.Select(c => c.Count));   // All, Wanted, Upcoming, Has candidates, Downloading, Failed, Needs details
        Assert.False(vm.HasNoQueue);
    }

    [Fact]
    public void Refresh_ShowsCandidatesBestFirst_WithPacksFlagged_UnderTheirIssueWhenExpanded()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        var issue = Issues(vm).Single(r => r.Title == "Spawn #261");
        Assert.Equal(new[] { "Spawn 261 (1992) cbz", "Spawn 261 (1992) cbr", "Spawn v1-6" }, issue.Candidates.Select(c => c.Title));
        Assert.Equal("45 MB · 20 seeders · IdxB", issue.Candidates[0].Detail);
        Assert.True(issue.Candidates[2].IsPack);
        Assert.Equal("2.0 GB · 9 seeders", issue.Candidates[2].Detail);
        Assert.True(issue.HasCandidateChip);
        Assert.False(Issues(vm).Single(r => r.Title == "Spawn #262").HasCandidateChip);
        Assert.DoesNotContain(vm.QueueItems, i => i is CandidateRowViewModel);            // collapsed until asked

        vm.ToggleCandidatesCommand.Execute(issue);

        int at = vm.QueueItems.IndexOf(issue);
        Assert.Equal(issue.Candidates, vm.QueueItems.Skip(at + 1).Take(3));               // right under the issue, best first
        Assert.True(issue.IsExpanded);
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

        Assert.True(vm.HasNoQueue);
        Assert.True(vm.HasNoSeries);
        Assert.False(vm.IsEnabled);
        Assert.True(vm.ShowSearchOffBanner);               // the persistent problem worth a banner

        vm.DismissSearchOffBannerCommand.Execute(null);
        Assert.False(vm.ShowSearchOffBanner);
    }

    [Fact]
    public void Tabs_SwitchTheActiveTab()
    {
        var vm = Create();
        Assert.True(vm.IsQueueTab);

        vm.GoSeriesCommand.Execute(null);
        Assert.True(vm.IsSeriesTab);
        Assert.False(vm.IsQueueTab);

        vm.GoReleasesCommand.Execute(null);
        Assert.True(vm.IsReleasesTab);
        vm.GoQueueCommand.Execute(null);
        Assert.True(vm.IsQueueTab);
    }

    [Fact]
    public void Ignore_MarksTheIssueIgnored_AndDropsItFromWanted()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        vm.IgnoreCommand.Execute(Issues(vm).First(r => r.Title == "Spawn #261"));

        Assert.Equal(2, vm.QueueCount);
        Assert.Empty(Issues(vm));                                          // the candidates went with #261, so the group no longer needs attention and closes
        vm.ToggleGroupCommand.Execute(vm.QueueItems.OfType<QueueGroupViewModel>().Single());
        Assert.Equal(new[] { "Spawn #262", "Spawn #263" }, Issues(vm).Select(r => r.Title));
        using var context = NewContext();
        Assert.Equal(WantedIssueStatus.Ignored, context.WantedIssues.Single(w => w.ExternalIssueId == 1).Status);
    }

    [Fact]
    public void Remove_DeletesTheWant_SoTheIssueReturnsToMissing()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        vm.RemoveCommand.Execute(Issues(vm).Single(r => r.Stage == QueueStage.Upcoming));

        Assert.DoesNotContain(Issues(vm), r => r.Title == "Spawn #263");
        using var context = NewContext();
        Assert.DoesNotContain(context.WantedIssues, w => w.ExternalIssueId == 3);
        var watched = context.WatchedSeries.Single();
        Assert.Contains(WantedService.GetMissing(context, watched), m => m.ExternalIssueId == 3);
    }

    [Fact]
    public async Task CopyLink_SendsTheDownloadUrlToTheClipboard()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        await vm.CopyLinkCommand.ExecuteAsync(Issues(vm).First(r => r.HasCandidateChip).Candidates[0]);

        Assert.Equal(new[] { "magnet:high" }, _copied);
        Assert.Contains(_toasts, t => !t.IsError && t.Message.StartsWith("Link copied"));
    }

    [Fact]
    public async Task SearchNow_RunsTheDaemon_ThenRefreshes_AndResetsTheBusyFlag()
    {
        var vm = Create();
        vm.Refresh();
        Seed();                                            // a candidate appears "during" the search
        Assert.True(vm.HasNoQueue);

        await vm.SearchNowCommand.ExecuteAsync(null);

        Assert.Equal(1, _searchNowCalls);
        Assert.False(vm.IsSearching);
        Assert.False(vm.HasNoQueue);                       // refreshed afterwards
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
        Assert.Contains(_toasts, t => t.Message.Contains("Following Spawn"));
    }

    [Fact]
    public async Task SeriesSearch_NeedsAComicVineKey_ThenListsResults_MarkingTrackedOnes()
    {
        var vm = Create();
        vm.SeriesSearchText = "Spawn";
        await vm.SearchSeriesCommand.ExecuteAsync(null);
        Assert.True(vm.HasSeriesSearchMessage);            // shown in the flyout, where the user is looking
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
        Assert.Contains(_toasts, t => t.Message.Contains("2 of 2 issues missing"));
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
        Assert.Equal("ComicVine rejected the API key.", vm.SeriesSearchMessage);

        vm.SeriesSearchMessage = string.Empty;
        await vm.TrackVolumeCommand.ExecuteAsync(new VolumeResultViewModel { Volume = new ComicVineVolume(1, "X", null, null, 0, null) });
        Assert.True(vm.HasSeriesSearchMessage);
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

    private int SeedFailedScrape(string number, string error, bool terminal)
    {
        using var context = NewContext();
        var watched = context.WatchedSeries.FirstOrDefault() ?? throw new InvalidOperationException("Seed() first.");
        var wanted = new WantedIssue
        {
            WatchedSeriesId = watched.Id, ExternalIssueId = 9000 + int.Parse(number), IssueNumber = number, CreatedAt = Today, Status = WantedIssueStatus.Imported,
            ScrapeStatus = ScrapeStatus.Failed, ScrapeError = error, ScrapeFailureIsTerminal = terminal, ScrapeAttempts = 2, ScrapeLastAttemptAt = Today,
        };
        context.WantedIssues.Add(wanted);
        context.SaveChanges();
        return wanted.Id;
    }

    [Fact]
    public void FailedScrapes_AppearInTheNeedsAttentionList_WithWhetherTheyNeedTheUser()
    {
        Seed();
        SeedFailedScrape("50", "ComicVine has no issue with this id.", terminal: true);
        SeedFailedScrape("51", "ComicVine request failed", terminal: false);
        var vm = Create();

        vm.Refresh();

        Assert.True(vm.HasScrapeReview);
        Assert.Equal(2, vm.ScrapeReviewRows.Count);
        Assert.Contains("2 issues", vm.ScrapeReviewHeading);
        Assert.True(vm.ScrapeReviewRows.Single(r => r.Title.EndsWith("#50")).NeedsYourAction);
        Assert.Equal("Paperbunkr will retry on its own", vm.ScrapeReviewRows.Single(r => r.Title.EndsWith("#51")).Hint);
    }

    [Fact]
    public void RetryAndDismiss_WorkOneAtATime_AndInBulk_AndLeaveTheIssueItself()
    {
        Seed();
        var one = SeedFailedScrape("50", "x", terminal: true);
        SeedFailedScrape("51", "y", terminal: false);
        SeedFailedScrape("52", "z", terminal: false);
        var vm = Create();
        vm.Refresh();

        vm.RetryScrapeCommand.Execute(vm.ScrapeReviewRows.Single(r => r.Id == one));
        using (var context = NewContext())
        {
            var retried = context.WantedIssues.Single(w => w.Id == one);
            Assert.Equal(ScrapeStatus.Pending, retried.ScrapeStatus);          // back in the sweep's queue right now
            Assert.False(retried.ScrapeFailureIsTerminal);
            Assert.Equal(WantedIssueStatus.Imported, retried.Status);          // the imported issue is untouched
        }

        Assert.Equal(2, vm.ScrapeReviewRows.Count);

        vm.DismissScrapeCommand.Execute(vm.ScrapeReviewRows[0]);
        Assert.Single(vm.ScrapeReviewRows);

        vm.RetryAllScrapesCommand.Execute(null);
        Assert.False(vm.HasScrapeReview);
        using var check = NewContext();
        Assert.Equal(0, check.WantedIssues.Count(w => w.ScrapeStatus == ScrapeStatus.Failed));
    }

    [Fact]
    public void DismissAll_ClearsTheListWithoutQueueingAnything()
    {
        Seed();
        SeedFailedScrape("50", "x", terminal: true);
        SeedFailedScrape("51", "y", terminal: false);
        var vm = Create();
        vm.Refresh();

        vm.DismissAllScrapesCommand.Execute(null);

        Assert.False(vm.HasScrapeReview);
        using var context = NewContext();
        Assert.Equal(0, context.WantedIssues.Count(w => w.ScrapeStatus == ScrapeStatus.Pending));
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
    public void TheQueueList_VirtualizesLongLists()
    {
        TestAppBuilder.EnsureInitialized();
        var vm = Create();
        for (int i = 0; i < 3000; i++)
        {
            vm.QueueItems.Add(new QueueIssueViewModel { Id = i, WatchedSeriesId = 1, SeriesName = "Series", IssueNumber = i.ToString(), StatusText = "Wanted" });
        }

        var view = new WantedScreen { DataContext = vm };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        window.GetLayoutManager()?.ExecuteLayoutPass();

        int RealizedRows() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<Avalonia.Controls.TextBlock>().Count(t => t.IsEffectivelyVisible && t.Text is { } x && x.StartsWith('#'));

        Assert.InRange(RealizedRows(), 1, 100);        // 3000 wants, one screenful realized
    }
}
