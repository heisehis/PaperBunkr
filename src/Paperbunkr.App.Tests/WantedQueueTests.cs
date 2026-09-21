using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>The Queue tab's grouping, sorting, filtering, expansion and in-place reconciling (docs/superpowers/specs/2026-09-21-wanted-screen-redesign-design.md 2).</summary>
public class WantedQueueTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 9, 19);
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_queue_{Guid.NewGuid():N}.db");
    private readonly List<string> _confirmations = new();
    private readonly List<(string Message, bool IsError)> _toasts = new();
    private bool _confirmAnswer = true;

    public WantedQueueTests()
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
        NewContext, _ => Task.CompletedTask, _ => { }, () => { }, _ => Task.CompletedTask,
        new GrabService(NewContext, _ => null, new ChannelEventPublisher()),
        post: a => a(), today: () => Today,
        notify: (message, isError) => _toasts.Add((message, isError)),
        confirm: (message, label) =>
        {
            _confirmations.Add(label);
            return Task.FromResult(_confirmAnswer);
        });

    private static IEnumerable<QueueIssueViewModel> Issues(WantedScreenViewModel vm) => vm.QueueItems.OfType<QueueIssueViewModel>();

    private static IEnumerable<QueueGroupViewModel> Groups(WantedScreenViewModel vm) => vm.QueueItems.OfType<QueueGroupViewModel>();

    /// <summary>Requests <paramref name="numbers"/> of a series as due wants (store dates in the past); returns the wanted ids by number.</summary>
    private Dictionary<string, int> SeedSeries(int volumeId, string name, params string[] numbers)
    {
        using var context = NewContext();
        var watched = WantedService.TrackVolume(context, new ComicVineVolume(volumeId, name, "Pub", 2000, 50, null), null, watchFutureReleases: false);
        var ids = new Dictionary<string, int>();
        int n = volumeId * 100;
        foreach (var number in numbers)
        {
            var wanted = WantedService.Request(context, watched, new ComicVineIssue(++n, number, null, Today.AddDays(-3), null, null, volumeId));
            ids[number] = wanted.Id;
        }

        return ids;
    }

    private void SetStatus(int wantedId, WantedIssueStatus status)
    {
        using var context = NewContext();
        var wanted = context.WantedIssues.Single(w => w.Id == wantedId);
        wanted.Status = status;
        if (status == WantedIssueStatus.Downloading)
        {
            wanted.DownloadProgress = 0.25;
            wanted.TorrentHash = new string('a', 40);
        }

        context.SaveChanges();
    }

    private void AddCandidate(int wantedId)
    {
        using var context = NewContext();
        context.ReleaseCandidates.Add(new ReleaseCandidate { WantedIssueId = wantedId, Title = "cand", DownloadUrl = "magnet:x", Score = 10, FoundAt = Today });
        context.SaveChanges();
    }

    [Fact]
    public void GroupsFollowTheSort_AttentionFirstByDefault_ThenAlphabetical_ThenRecentlyAdded()
    {
        var aardvark = SeedSeries(1, "Aardvark", "1");
        var zed = SeedSeries(2, "Zed", "1");
        SetStatus(zed["1"], WantedIssueStatus.Failed);
        var vm = Create();
        vm.Refresh();

        Assert.Equal(new[] { "Zed", "Aardvark" }, Groups(vm).Select(g => g.Name));                 // a failure jumps the queue

        vm.QueueSortText = "A–Z";
        Assert.Equal(new[] { "Aardvark", "Zed" }, Groups(vm).Select(g => g.Name));

        using (var context = NewContext())
        {
            context.WantedIssues.Single(w => w.Id == aardvark["1"]).CreatedAt = Today.AddDays(5);
            context.SaveChanges();
        }

        vm.Refresh();
        vm.QueueSortText = "Recently added";
        Assert.Equal("Aardvark", Groups(vm).First().Name);
    }

    [Fact]
    public void AGroupOpensOnItsOwn_OnlyWhenItNeedsTheUser_AndTheUsersChoiceSticks()
    {
        SeedSeries(1, "Aardvark", "1", "2");
        var zed = SeedSeries(2, "Zed", "1");
        AddCandidate(zed["1"]);
        var vm = Create();
        vm.Refresh();

        var aardvark = Groups(vm).Single(g => g.Name == "Aardvark");
        Assert.False(aardvark.IsExpanded);
        Assert.DoesNotContain(Issues(vm), i => i.Title.StartsWith("Aardvark"));
        Assert.Contains(Issues(vm), i => i.Title == "Zed #1");                                     // has a candidate to review
        Assert.Equal("2 wanted", aardvark.Summary);

        vm.ToggleGroupCommand.Execute(aardvark);
        Assert.Equal(new[] { "Aardvark #1", "Aardvark #2" }, Issues(vm).Where(i => i.Title.StartsWith("Aardvark")).Select(i => i.Title));

        vm.Refresh();                                                                              // a reload keeps what the user opened
        Assert.True(Groups(vm).Single(g => g.Name == "Aardvark").IsExpanded);

        vm.ToggleGroupCommand.Execute(Groups(vm).Single(g => g.Name == "Zed"));                    // and closes what they closed, even one that wants attention
        vm.Refresh();
        Assert.DoesNotContain(Issues(vm), i => i.Title == "Zed #1");
    }

    [Fact]
    public void AStageChip_ShowsOnlyThatStage_AndOpensEveryGroupItMatches()
    {
        SeedSeries(1, "Aardvark", "1");
        var zed = SeedSeries(2, "Zed", "1", "2");
        SetStatus(zed["2"], WantedIssueStatus.Failed);
        var vm = Create();
        vm.Refresh();

        vm.SetQueueFilterCommand.Execute(vm.QueueChips.Single(c => c.Filter == QueueFilter.Failed));

        Assert.Equal(new[] { "Zed #2" }, Issues(vm).Select(i => i.Title));
        Assert.Equal(new[] { "Zed" }, Groups(vm).Select(g => g.Name));
        Assert.True(vm.QueueChips.Single(c => c.Filter == QueueFilter.Failed).IsActive);
        Assert.False(vm.QueueChips.Single(c => c.Filter == QueueFilter.All).IsActive);
        Assert.Equal(3, vm.QueueCount);                                                            // the tab still counts everything

        vm.SetQueueFilterCommand.Execute(vm.QueueChips.Single(c => c.Filter == QueueFilter.Downloading));
        Assert.Empty(vm.QueueItems);
        Assert.True(vm.HasNoQueueMatches);
        Assert.False(vm.HasNoQueue);
    }

    [Fact]
    public void AnEmptyStageChip_StaysHidden_UnlessItIsInUse()
    {
        SeedSeries(1, "Aardvark", "1");
        var vm = Create();
        vm.Refresh();

        Assert.True(vm.QueueChips.Single(c => c.Filter == QueueFilter.All).IsVisible);
        Assert.True(vm.QueueChips.Single(c => c.Filter == QueueFilter.Wanted).IsVisible);
        Assert.False(vm.QueueChips.Single(c => c.Filter == QueueFilter.Failed).IsVisible);

        vm.QueueFilter = QueueFilter.Failed;
        Assert.True(vm.QueueChips.Single(c => c.Filter == QueueFilter.Failed).IsVisible);
    }

    [Fact]
    public void AReload_KeepsEveryRowInstanceInPlace_EvenWhenAnIssueChangesStage()
    {
        var zed = SeedSeries(2, "Zed", "1", "2", "3");
        SetStatus(zed["1"], WantedIssueStatus.Failed);                                            // keeps the group open
        var vm = Create();
        vm.Refresh();
        var before = vm.QueueItems.ToList();
        var second = Issues(vm).Single(i => i.Title == "Zed #2");
        Assert.Equal(QueueStage.Wanted, second.Stage);

        SetStatus(zed["2"], WantedIssueStatus.Downloading);
        vm.Refresh();

        Assert.Equal(before.Count, vm.QueueItems.Count);
        Assert.All(before.Zip(vm.QueueItems), pair => Assert.Same(pair.First, pair.Second));       // nothing was rebuilt, so nothing jumped
        Assert.Equal(QueueStage.Downloading, second.Stage);
        Assert.Same(second, Assert.Single(vm.DownloadStrip));
    }

    [Fact]
    public void ARowThatLeaves_IsRemovedWithoutDisturbingTheOthers()
    {
        var zed = SeedSeries(2, "Zed", "1", "2", "3");
        SetStatus(zed["1"], WantedIssueStatus.Failed);
        var vm = Create();
        vm.Refresh();
        var keep = Issues(vm).Where(i => i.Title != "Zed #2").ToList();

        using (var context = NewContext())
        {
            context.WantedIssues.Remove(context.WantedIssues.Single(w => w.Id == zed["2"]));
            context.SaveChanges();
        }

        vm.Refresh();

        Assert.Equal(keep, Issues(vm));
        Assert.Equal(2, vm.QueueCount);
    }

    [Fact]
    public void IssuesInAGroup_AreOrderedByIssueNumber_NotByText()
    {
        var zed = SeedSeries(2, "Zed", "105", "16", "2");
        SetStatus(zed["16"], WantedIssueStatus.Failed);
        var vm = Create();
        vm.Refresh();

        Assert.Equal(new[] { "Zed #2", "Zed #16", "Zed #105" }, Issues(vm).Select(i => i.Title));
    }

    [Fact]
    public void IWantAllOfThese_AsksFirst_ThenMarksTheListedIssuesAsOwned_LeavingADownloadAlone()
    {
        var zed = SeedSeries(2, "Zed", "1", "2", "3");
        SetStatus(zed["3"], WantedIssueStatus.Downloading);
        var vm = Create();
        vm.Refresh();
        var group = Groups(vm).Single();
        Assert.True(group.CanBulk);

        _confirmAnswer = false;
        vm.IgnoreGroupCommand.Execute(group);
        Assert.Equal(new[] { "I have all" }, _confirmations);
        Assert.Equal(3, vm.QueueCount);                                                            // declined: nothing changed

        _confirmAnswer = true;
        vm.IgnoreGroupCommand.Execute(group);

        using var context = NewContext();
        Assert.Equal(2, context.WantedIssues.Count(w => w.Status == WantedIssueStatus.Ignored));
        Assert.Equal(WantedIssueStatus.Downloading, context.WantedIssues.Single(w => w.Id == zed["3"]).Status);
        Assert.Equal(new[] { "Zed #3" }, Issues(vm).Select(i => i.Title));
        Assert.Contains(_toasts, t => t.Message.StartsWith("Marked 2 issues of Zed"));
    }

    [Fact]
    public void RemoveAll_DropsTheListedWants_AndRespectsTheChipInUse()
    {
        var zed = SeedSeries(2, "Zed", "1", "2");
        SetStatus(zed["2"], WantedIssueStatus.Failed);
        var vm = Create();
        vm.Refresh();
        vm.QueueFilter = QueueFilter.Wanted;                                                       // only #1 is listed, so only #1 is touched

        vm.RemoveGroupCommand.Execute(Groups(vm).Single());

        using var context = NewContext();
        Assert.Equal(new[] { zed["2"] }, context.WantedIssues.Select(w => w.Id));
        Assert.Contains(_toasts, t => t.Message.StartsWith("Stopped wanting 1 issue of Zed"));
    }

    [Fact]
    public void NeedsDetails_ListsTheFailedScrapes_InsteadOfTheGroups()
    {
        var zed = SeedSeries(2, "Zed", "1");
        using (var context = NewContext())
        {
            var w = context.WantedIssues.Single(x => x.Id == zed["1"]);
            w.Status = WantedIssueStatus.Imported;
            w.ScrapeStatus = ScrapeStatus.Failed;
            w.ScrapeError = "no such issue";
            context.SaveChanges();
        }

        var vm = Create();
        vm.Refresh();
        Assert.True(vm.HasScrapeReview);
        Assert.Equal(1, vm.QueueChips.Single(c => c.Filter == QueueFilter.NeedsDetails).Count);

        vm.QueueFilter = QueueFilter.NeedsDetails;

        Assert.IsType<ScrapeReviewRowViewModel>(Assert.Single(vm.QueueItems));
        Assert.True(vm.IsNeedsDetailsFilter);
    }

    [Fact]
    public void Untrack_AsksFirst_ThenRemovesTheSeriesAndItsWants()
    {
        var zed = SeedSeries(2, "Zed", "1", "2");
        SeedSeries(3, "Other", "1");
        var vm = Create();
        vm.Refresh();
        var row = vm.SeriesRows.Single(r => r.Name == "Zed");

        _confirmAnswer = false;
        vm.UntrackSeriesCommand.Execute(row);
        Assert.Equal(2, vm.SeriesRows.Count);

        _confirmAnswer = true;
        vm.UntrackSeriesCommand.Execute(row);

        Assert.Equal(new[] { "Untrack", "Untrack" }, _confirmations);
        Assert.Equal("Other", Assert.Single(vm.SeriesRows).Name);
        Assert.DoesNotContain(Issues(vm).Concat(vm.QueueItems.OfType<QueueIssueViewModel>()), i => i.Id == zed["1"]);
        using var context = NewContext();
        Assert.Equal(1, context.WatchedSeries.Count());
        Assert.Equal(1, context.WantedIssues.Count());                                             // cascaded
        Assert.Contains(_toasts, t => t.Message == "No longer tracking Zed.");
    }

    [Fact]
    public void ASeriesReload_KeepsTheRowsAndNeverFiresTheFollowSwitch()
    {
        SeedSeries(2, "Zed", "1");
        var vm = Create();
        vm.Refresh();
        var row = vm.SeriesRows.Single();

        using (var context = NewContext())
        {
            context.WatchedSeries.Single().WatchFutureReleases = true;
            context.SaveChanges();
        }

        vm.Refresh();

        Assert.Same(row, vm.SeriesRows.Single());
        Assert.True(row.WatchFutureReleases);                                                      // took the stored value
        Assert.Empty(_toasts);                                                                     // ...without looking like a click
    }

    [Fact]
    public void ASeriesWithoutACover_BorrowsItsFirstIssuesCover_InTheQueueAndTheSeriesList()
    {
        var zed = SeedSeries(2, "Zed", "1", "2");
        using (var context = NewContext())
        {
            context.WantedIssues.Single(w => w.Id == zed["2"]).CoverImageUrl = "https://example.test/zed-2.jpg";
            context.SaveChanges();
        }

        var vm = Create();
        vm.Refresh();

        Assert.True(Groups(vm).Single().Cover.HasCover);
        Assert.True(vm.SeriesRows.Single().Cover.HasCover);
    }

    [Fact]
    public void AMetronSeriesWithNoCoverAnywhere_FallsBackToTheWeeklyListsCachedCover()
    {
        using (var context = NewContext())
        {
            var watched = WantedService.TrackVolume(context, new ComicVineVolume(77, "Green Lantern", "DC", 2023, 0, null), null, watchFutureReleases: false, ComicProvider.Metron);
            WantedService.Request(context, watched, new ComicVineIssue(1, "16", null, Today.AddDays(-3), null, null, 77));
            context.PullListReleases.Add(new PullListRelease { Provider = ComicProvider.Metron, ExternalIssueId = 500, SeriesId = 77, SeriesName = "Green Lantern", IssueNumber = "22",
                StoreDate = Today, CoverImageUrl = "https://example.test/gl-22.jpg", FetchedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        var vm = Create();
        vm.Refresh();

        Assert.True(Groups(vm).Single().Cover.HasCover);
        Assert.True(vm.SeriesRows.Single().Cover.HasCover);
    }

    [Fact]
    public void TheMenu_OffersWhatTheRowCanDo()
    {
        var zed = SeedSeries(2, "Zed", "1", "2");
        SetStatus(zed["2"], WantedIssueStatus.Downloading);
        AddCandidate(zed["1"]);
        var vm = Create();
        vm.Refresh();

        var wanted = vm.BuildContextMenu(Issues(vm).Single(i => i.Title == "Zed #1"))!;
        Assert.Equal(new[] { "Show 1 candidate", "I have this", "Stop wanting this" }, wanted.Select(e => e.Header));

        var downloading = vm.BuildContextMenu(Issues(vm).Single(i => i.Title == "Zed #2"))!;
        Assert.Equal(new[] { "Cancel download" }, downloading.Select(e => e.Header));

        var group = vm.BuildContextMenu(Groups(vm).Single())!;
        Assert.Equal(new[] { "Collapse", "I have all of these", "Stop wanting all of these" }, group.Select(e => e.Header));
    }
}
