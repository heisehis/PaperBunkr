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
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>The Wanted screen's Releases tab (docs/superpowers/specs/2026-09-20-weekly-pull-list-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class WantedReleasesTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 9, 23);     // a Wednesday
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_releases_{Guid.NewGuid():N}.db");

    public WantedReleasesTests()
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
        _ => Task.CompletedTask,
        _ => { },
        () => { },
        _ => Task.CompletedTask,
        new GrabService(NewContext, _ => null, new ChannelEventPublisher()),
        post: a => a(),
        today: () => Today);

    private void Seed()
    {
        using var context = NewContext();
        context.PullListReleases.AddRange(
            new PullListRelease { ExternalIssueId = 1, SeriesId = 10, SeriesName = "Spawn", IssueNumber = "349", StoreDate = Today.AddDays(-5), FetchedAt = DateTime.UtcNow },
            new PullListRelease { ExternalIssueId = 2, SeriesId = 10, SeriesName = "Spawn", IssueNumber = "350", StoreDate = Today.AddDays(1), FetchedAt = DateTime.UtcNow },
            new PullListRelease { ExternalIssueId = 3, SeriesId = 20, SeriesName = "Batman", IssueNumber = "1", StoreDate = Today.AddDays(8), FetchedAt = DateTime.UtcNow },
            new PullListRelease { ExternalIssueId = 4, SeriesId = 20, SeriesName = "Batman", IssueNumber = "2", StoreDate = Today.AddDays(15), FetchedAt = DateTime.UtcNow });
        context.MetronSeries.AddRange(
            new MetronSeriesInfo { SeriesId = 10, Name = "Spawn", Publisher = "Image", YearBegan = 1992, FetchedAt = DateTime.UtcNow },
            new MetronSeriesInfo { SeriesId = 20, Name = "Batman", Publisher = "DC Comics", YearBegan = 2016, FetchedAt = DateTime.UtcNow });
        context.SaveChanges();
    }

    [Fact]
    public void TheTabGroupsReleasesByWeek_AndListsThePublishersSeen()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        Assert.Equal(new[] { "Last week", "This week", "Next week", "Week of Oct 5" }, vm.ReleaseWeeks.Select(w => w.Title));
        Assert.Equal(4, vm.ReleaseCount);
        Assert.False(vm.HasNoReleases);
        Assert.Equal(new[] { "All publishers", "DC Comics", "Image" }, vm.ReleasePublisherNames);
        var row = vm.ReleaseWeeks[1].Rows.Single();
        Assert.Equal("Spawn #350", row.Title);
        Assert.Contains("Image", row.Subtitle);
    }

    [Fact]
    public void ThePublisherAndFollowedOnlyFiltersNarrowTheList()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        vm.ReleasePublisherText = "DC Comics";
        Assert.Equal(2, vm.ReleaseCount);
        Assert.All(vm.ReleaseWeeks.SelectMany(w => w.Rows), r => Assert.StartsWith("Batman", r.Title));

        vm.ReleasePublisherText = WantedScreenViewModel.AllPublishers;
        vm.ReleasesFollowedOnly = true;
        Assert.Equal(0, vm.ReleaseCount);                    // nothing is followed yet
        Assert.True(vm.HasNoReleases);
        Assert.Equal("Nothing matches these filters.", vm.ReleasesEmptyText);
    }

    [Fact]
    public void WithNothingFetched_TheTabExplainsWhy()
    {
        var vm = Create();
        vm.Refresh();
        Assert.True(vm.HasNoReleases);
        Assert.Contains("Metron login", vm.ReleasesEmptyText);

        using (var context = NewContext())
        {
            CredentialStore.Set(context, "Metron", CredentialKind.Username, "reader");
            CredentialStore.Set(context, "Metron", CredentialKind.Password, "pw");
        }

        vm.Refresh();
        Assert.Contains("Nothing fetched yet", vm.ReleasesEmptyText);
    }

    [Fact]
    public void Request_TracksTheSeriesOnMetron_AndMakesJustThatIssueWanted()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        var row = vm.ReleaseWeeks.SelectMany(w => w.Rows).Single(r => r.Title == "Spawn #350");
        Assert.True(row.CanRequest);
        Assert.True(row.CanFollow);

        vm.RequestReleaseCommand.Execute(row);

        using var context = NewContext();
        var watched = context.WatchedSeries.Single();
        Assert.Equal(ComicProvider.Metron, watched.Provider);
        Assert.Equal(10, watched.ExternalVolumeId);
        Assert.Equal("Image", watched.Publisher);
        Assert.False(watched.WatchFutureReleases);                       // requesting is not following
        var wanted = Assert.Single(context.WantedIssues);
        Assert.Equal("350", wanted.IssueNumber);

        var refreshed = vm.ReleaseWeeks.SelectMany(w => w.Rows).Single(r => r.Title == "Spawn #350");
        Assert.False(refreshed.CanRequest);                              // already wanted
        Assert.Equal("Wanted", refreshed.StatusText);
    }

    [Fact]
    public void Follow_TracksAndFollows_AndRequestsTheUpcomingIssues()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        vm.FollowReleaseSeriesCommand.Execute(vm.ReleaseWeeks.SelectMany(w => w.Rows).First(r => r.Title == "Batman #1"));

        using var context = NewContext();
        var watched = context.WatchedSeries.Single();
        Assert.True(watched.WatchFutureReleases);
        Assert.Equal(new[] { "1", "2" }, context.WantedIssues.AsEnumerable().Select(w => w.IssueNumber).OrderBy(n => n));
        Assert.All(vm.ReleaseWeeks.SelectMany(w => w.Rows).Where(r => r.Title.StartsWith("Batman")), r => Assert.True(r.IsFollowed));
        Assert.True(vm.HasInfoStatus);
    }

    [Fact]
    public void HidingARelease_RemovesItFromTheList_UntilShowHiddenIsOn_AndRestoreBringsItBack()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        var row = vm.ReleaseWeeks.SelectMany(w => w.Rows).Single(r => r.Title == "Spawn #350");
        Assert.True(row.CanHide);

        vm.HideReleaseCommand.Execute(row);

        Assert.Equal(3, vm.ReleaseCount);
        Assert.DoesNotContain(vm.ReleaseWeeks.SelectMany(w => w.Rows), r => r.Title == "Spawn #350");
        using (var context = NewContext())
        {
            Assert.True(context.PullListReleases.Single(r => r.ExternalIssueId == 2).IsHidden);
        }

        vm.ReleasesShowHidden = true;
        var hidden = vm.ReleaseWeeks.SelectMany(w => w.Rows).Single(r => r.Title == "Spawn #350");
        Assert.Equal("Hidden", hidden.StatusText);
        Assert.False(hidden.CanRequest);                                    // a hidden release offers Restore, not Request or Follow
        Assert.False(hidden.CanFollow);

        vm.RestoreReleaseCommand.Execute(hidden);
        vm.ReleasesShowHidden = false;
        Assert.Equal(4, vm.ReleaseCount);
    }
}
