using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.VisualTree;
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

/// <summary>The Wanted screen's Releases tab: a cover shelf, one week per page, with a calendar (docs/superpowers/specs/2026-09-21-wanted-screen-redesign-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class WantedReleasesTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 9, 23);     // a Wednesday; this week is Sep 21 - 27
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_releases_{Guid.NewGuid():N}.db");
    private readonly List<(string Message, bool IsError)> _toasts = new();
    private readonly List<(DateTime From, DateTime To)> _fetches = new();
    private Func<DateTime, DateTime, Task<bool>> _fetch = (_, _) => Task.FromResult(false);

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
        today: () => Today,
        notify: (message, isError) => _toasts.Add((message, isError)),
        fetchReleases: (from, to, ct) =>
        {
            _fetches.Add((from, to));
            return _fetch(from, to);
        });

    private void Seed()
    {
        using var context = NewContext();
        context.PullListReleases.AddRange(
            new PullListRelease { ExternalIssueId = 1, SeriesId = 10, SeriesName = "Spawn", IssueNumber = "349", StoreDate = Today.AddDays(-5), FetchedAt = DateTime.UtcNow },
            new PullListRelease { ExternalIssueId = 2, SeriesId = 10, SeriesName = "Spawn", IssueNumber = "350", StoreDate = Today.AddDays(1), FetchedAt = DateTime.UtcNow },
            new PullListRelease { ExternalIssueId = 3, SeriesId = 20, SeriesName = "Batman", IssueNumber = "1", StoreDate = Today.AddDays(8), FetchedAt = DateTime.UtcNow },
            new PullListRelease { ExternalIssueId = 4, SeriesId = 20, SeriesName = "Batman", IssueNumber = "2", StoreDate = Today.AddDays(15), FetchedAt = DateTime.UtcNow });
        context.ReleaseSeries.AddRange(
            new ReleaseSeriesInfo { Provider = ComicProvider.Metron, SeriesId = 10, Name = "Spawn", Publisher = "Image", YearBegan = 1992, FetchedAt = DateTime.UtcNow },
            new ReleaseSeriesInfo { Provider = ComicProvider.Metron, SeriesId = 20, Name = "Batman", Publisher = "DC Comics", YearBegan = 2016, FetchedAt = DateTime.UtcNow });
        context.GetOrCreateAcquisitionSettings().PullListRefreshedAt = DateTime.UtcNow.AddHours(-2);
        context.SaveChanges();
    }

    private static IEnumerable<ReleaseRowViewModel> Tiles(WantedScreenViewModel vm) => vm.ReleaseDays.SelectMany(d => d.Rows);

    [Fact]
    public void TheShelfShowsOneWeekAtATime_StartingWithThisWeek_GroupedByDay()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        Assert.Equal(new DateTime(2026, 9, 21), vm.ReleaseWeekStart);
        Assert.Equal("Sep 21 – 27", vm.ReleaseWeekLabel);
        Assert.Equal("This week", vm.ReleaseWeekCaption);
        Assert.True(vm.IsViewingThisWeek);
        var day = Assert.Single(vm.ReleaseDays);
        Assert.Equal(new DateTime(2026, 9, 24), day.Date);
        Assert.Equal("1 release", day.CountText);
        var tile = Assert.Single(day.Rows);
        Assert.Equal("Spawn #350", tile.Title);
        Assert.Equal("Image", tile.Subtitle);
        Assert.Equal(1, vm.ReleaseCount);
        Assert.False(vm.HasNoReleases);
        Assert.Equal(new[] { "All publishers", "DC Comics", "Image" }, vm.ReleasePublisherNames);
        Assert.Contains("updated", vm.ReleaseFreshnessText);
    }

    [Fact]
    public async Task TheArrowsAndTodayStepThroughWeeks_WithoutFetchingWhatIsAlreadyCached()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        await vm.NextWeekCommand.ExecuteAsync(null);
        Assert.Equal("Next week", vm.ReleaseWeekCaption);
        Assert.Equal("Batman #1", Assert.Single(Tiles(vm)).Title);

        await vm.PreviousWeekCommand.ExecuteAsync(null);
        await vm.PreviousWeekCommand.ExecuteAsync(null);
        Assert.Equal("Last week", vm.ReleaseWeekCaption);
        Assert.Equal("Spawn #349", Assert.Single(Tiles(vm)).Title);

        await vm.ThisWeekCommand.ExecuteAsync(null);
        Assert.True(vm.IsViewingThisWeek);
        Assert.Equal("Spawn #350", Assert.Single(Tiles(vm)).Title);
        Assert.Equal(new[] { (new DateTime(2026, 9, 14), new DateTime(2026, 9, 20)) }, _fetches);   // only last week reaches outside the cached window
    }

    [Fact]
    public async Task ThePublisherAndFollowedOnlyFiltersNarrowTheWeek()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        vm.ReleasePublisherText = "DC Comics";
        Assert.Equal(0, vm.ReleaseCount);                    // this week only has Spawn (Image)
        Assert.True(vm.HasNoReleases);
        Assert.Equal("Nothing matches these filters.", vm.ReleasesEmptyText);

        await vm.NextWeekCommand.ExecuteAsync(null);
        Assert.Equal("Batman #1", Assert.Single(Tiles(vm)).Title);

        vm.ReleasePublisherText = WantedScreenViewModel.AllPublishers;
        vm.ReleasesFollowedOnly = true;
        Assert.Equal(0, vm.ReleaseCount);                    // nothing is followed yet
        Assert.True(vm.HasNoReleases);
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
    public void AWeekWithNoReleases_SaysSo()
    {
        Seed();
        using (var context = NewContext())
        {
            CredentialStore.Set(context, "Metron", CredentialKind.Username, "reader");
            CredentialStore.Set(context, "Metron", CredentialKind.Password, "pw");
            context.PullListReleases.RemoveRange(context.PullListReleases.Where(r => r.ExternalIssueId == 2));
            context.SaveChanges();
        }

        var vm = Create();
        vm.Refresh();

        Assert.True(vm.HasNoReleases);
        Assert.Equal("No releases this week.", vm.ReleasesEmptyText);
    }

    [Fact]
    public void Request_TracksTheSeriesOnMetron_AndMakesJustThatIssueWanted()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        var row = Tiles(vm).Single(r => r.Title == "Spawn #350");
        Assert.True(row.CanRequest);
        Assert.True(row.CanFollow);
        Assert.True(row.PrimaryIsRequest);
        Assert.True(row.MenuHasFollow);                                   // Follow waits in the overflow menu while Request holds the button

        vm.RequestReleaseCommand.Execute(row);

        using var context = NewContext();
        var watched = context.WatchedSeries.Single();
        Assert.Equal(ComicProvider.Metron, watched.Provider);
        Assert.Equal(10, watched.ExternalVolumeId);
        Assert.Equal("Image", watched.Publisher);
        Assert.False(watched.WatchFutureReleases);                       // requesting is not following
        var wanted = Assert.Single(context.WantedIssues);
        Assert.Equal("350", wanted.IssueNumber);

        var refreshed = Tiles(vm).Single(r => r.Title == "Spawn #350");
        Assert.False(refreshed.CanRequest);                              // already wanted
        Assert.Equal("Wanted", refreshed.StatusText);
        Assert.True(refreshed.PrimaryIsFollow);                          // the button now offers the next useful thing
        Assert.Contains(_toasts, t => t.Message == "Requested Spawn #350.");
    }

    [Fact]
    public async Task Follow_TracksAndFollows_AndRequestsTheUpcomingIssues()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        await vm.NextWeekCommand.ExecuteAsync(null);

        vm.FollowReleaseSeriesCommand.Execute(Tiles(vm).Single(r => r.Title == "Batman #1"));

        using var context = NewContext();
        var watched = context.WatchedSeries.Single();
        Assert.True(watched.WatchFutureReleases);
        Assert.Equal(new[] { "1", "2" }, context.WantedIssues.AsEnumerable().Select(w => w.IssueNumber).OrderBy(n => n));
        Assert.All(Tiles(vm), r => Assert.True(r.IsFollowed));
        Assert.Contains(_toasts, t => t.Message.StartsWith("Following Batman"));
    }

    [Fact]
    public void HidingARelease_RemovesItFromTheShelf_UntilShowHiddenIsOn_AndRestoreBringsItBack()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        var row = Tiles(vm).Single(r => r.Title == "Spawn #350");
        Assert.True(row.CanHide);

        vm.HideReleaseCommand.Execute(row);

        Assert.Equal(0, vm.ReleaseCount);
        Assert.DoesNotContain(Tiles(vm), r => r.Title == "Spawn #350");
        using (var context = NewContext())
        {
            Assert.True(context.PullListReleases.Single(r => r.ExternalIssueId == 2).IsHidden);
        }

        vm.ReleasesShowHidden = true;
        var hidden = Tiles(vm).Single(r => r.Title == "Spawn #350");
        Assert.Equal("Hidden", hidden.StatusText);
        Assert.False(hidden.CanRequest);                                    // a hidden release offers Restore, not Request or Follow
        Assert.False(hidden.CanFollow);
        Assert.False(hidden.PrimaryIsRequest);
        Assert.False(hidden.PrimaryIsFollow);

        vm.RestoreReleaseCommand.Execute(hidden);
        vm.ReleasesShowHidden = false;
        Assert.Equal(1, vm.ReleaseCount);
    }

    [Fact]
    public void TheCalendar_DotsTheDaysWithReleases_AndFollowedOnes()
    {
        Seed();
        using (var context = NewContext())
        {
            WantedService.TrackVolume(context, new ComicVineVolume(20, "Batman", "DC Comics", 2016, 0, null), null, watchFutureReleases: true, ComicProvider.Metron);
        }

        var vm = Create();
        vm.Refresh();

        Assert.Equal("September 2026", vm.CalendarTitle);
        Assert.Equal(42, vm.CalendarDays.Count);
        Assert.Equal(DayOfWeek.Monday, vm.CalendarDays[0].Date.DayOfWeek);
        var spawnDay = vm.CalendarDays.Single(d => d.Date == new DateTime(2026, 9, 24));
        Assert.True(spawnDay.HasReleases);
        Assert.False(spawnDay.HasFollowed);
        Assert.True(spawnDay.IsInWeek);
        var batmanDay = vm.CalendarDays.Single(d => d.Date == new DateTime(2026, 10, 1));
        Assert.True(batmanDay.HasReleases);
        Assert.True(batmanDay.HasFollowed);
        Assert.False(vm.CalendarDays.Single(d => d.Date == new DateTime(2026, 9, 22)).HasReleases);
        Assert.True(vm.CalendarDays.Single(d => d.Date == Today).IsToday);

        vm.NextMonthCommand.Execute(null);
        Assert.Equal("October 2026", vm.CalendarTitle);
        Assert.True(vm.CalendarDays.Single(d => d.Date == new DateTime(2026, 10, 8)).HasReleases);
    }

    [Fact]
    public async Task PickingADate_ClosesTheCalendar_AndShowsThatWeek()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        vm.ToggleCalendarCommand.Execute(null);
        Assert.True(vm.IsCalendarOpen);

        await vm.PickDateCommand.ExecuteAsync(vm.CalendarDays.Single(d => d.Date == new DateTime(2026, 10, 1)));

        Assert.False(vm.IsCalendarOpen);
        Assert.Equal(new DateTime(2026, 9, 28), vm.ReleaseWeekStart);
        Assert.Equal("Sep 28 – Oct 4", vm.ReleaseWeekLabel);
        Assert.Equal("Batman #1", Assert.Single(Tiles(vm)).Title);
        Assert.Empty(_fetches);                                             // inside the cached window
    }

    [Fact]
    public async Task AWeekOutsideTheCachedWindow_IsFetchedOnce_AndFillsInWhenItArrives()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        _fetch = (from, to) =>
        {
            using var context = NewContext();
            context.PullListReleases.Add(new PullListRelease { ExternalIssueId = 99, SeriesId = 30, SeriesName = "Saga", IssueNumber = "70", StoreDate = new DateTime(2026, 11, 18), FetchedAt = DateTime.UtcNow });
            context.SaveChanges();
            return Task.FromResult(true);
        };

        vm.CalendarMonth = new DateTime(2026, 11, 1);
        vm.NextMonthCommand.Execute(null);                                   // through the month buttons, then pick a day
        vm.PreviousMonthCommand.Execute(null);
        await vm.PickDateCommand.ExecuteAsync(vm.CalendarDays.Single(d => d.Date == new DateTime(2026, 11, 18)));

        Assert.Equal(new[] { (new DateTime(2026, 11, 16), new DateTime(2026, 11, 22)) }, _fetches);
        Assert.Equal("Saga #70", Assert.Single(Tiles(vm)).Title);
        Assert.False(vm.IsReleasesLoading);

        await vm.PickDateCommand.ExecuteAsync(vm.CalendarDays.Single(d => d.Date == new DateTime(2026, 11, 19)));
        Assert.Single(_fetches);                                             // the same week again: already fetched
    }

    [Fact]
    public async Task AFailedWeekFetch_SaysSo_AndIsTriedAgainNextVisit()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        _fetch = (_, _) => throw new ComicVineException("Metron is rate limiting requests.", 107);

        await vm.PickDateCommand.ExecuteAsync(new CalendarDayViewModel { Date = new DateTime(2026, 12, 2) });

        Assert.True(vm.ReleaseLoadFailed);
        Assert.Contains("Couldn't load this week", vm.ReleasesEmptyText);
        Assert.Contains(_toasts, t => t.IsError && t.Message.Contains("rate limiting"));
        Assert.False(vm.IsReleasesLoading);

        _fetch = (_, _) => Task.FromResult(true);
        await vm.PickDateCommand.ExecuteAsync(new CalendarDayViewModel { Date = new DateTime(2026, 12, 3) });
        Assert.Equal(2, _fetches.Count);
        Assert.False(vm.ReleaseLoadFailed);
    }

    [Fact]
    public void ARefreshThatChangesNothingInTheWeek_LeavesTheShelfAlone()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        var day = vm.ReleaseDays.Single();

        vm.Refresh();                                                        // a download ticking along elsewhere refreshes this screen every second

        Assert.Same(day, vm.ReleaseDays.Single());
    }

    /// <summary>Avalonia has no virtualizing wrap panel, so a busy Wednesday's tiles are all realized; a 150-tile week must still lay out.</summary>
    [Fact]
    public void ABusyWeekOf150Tiles_LaysOutWithoutTrouble()
    {
        TestAppBuilder.EnsureInitialized();
        var vm = Create();
        vm.GoReleasesCommand.Execute(null);                                   // opening the tab reloads the shelf from the database, so add the tiles after
        var rows = Enumerable.Range(0, 150).Select(i => new ReleaseRowViewModel { ReleaseId = i, SeriesId = i, Title = $"Series {i} #1", Subtitle = "Pub" }).ToList();
        vm.ReleaseDays.Add(new ReleaseDayViewModel { Date = new DateTime(2026, 9, 23), Rows = rows });

        var view = new Paperbunkr.App.Views.WantedScreen { DataContext = vm };
        // The headless test app has no theme, so a ToggleSwitch's template parts are missing; they are not what this test is about.
        foreach (var toggle in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(view).OfType<Avalonia.Controls.ToggleSwitch>().ToList())
        {
            toggle.IsVisible = false;
        }

        var window = new Avalonia.Controls.Window { Content = view, Width = 1000, Height = 700 };

        // ...and neither is the theme's ItemsControl template (the day and tile lists use it); give those the bare presenter, which keeps the view's own WrapPanel.
        var bare = new Style(x => x.OfType<Avalonia.Controls.ItemsControl>());
        bare.Setters.Add(new Setter(Avalonia.Controls.Primitives.TemplatedControl.TemplateProperty,
            new FuncControlTemplate<Avalonia.Controls.ItemsControl>((_, scope) =>
                new Avalonia.Controls.Presenters.ItemsPresenter { Name = "PART_ItemsPresenter", [~Avalonia.Controls.Presenters.ItemsPresenter.ItemsPanelProperty] = new Avalonia.Data.TemplateBinding(Avalonia.Controls.ItemsControl.ItemsPanelProperty) }
                    .RegisterInNameScope(scope))));
        window.Styles.Add(bare);
        window.Show();
        window.GetLayoutManager()?.ExecuteLayoutPass();

        int tiles = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<Avalonia.Controls.TextBlock>().Count(t => t.Text is { } x && x.StartsWith("Series ", StringComparison.Ordinal));
        Assert.Equal(150, tiles);
        window.Close();
    }

    [Fact]
    public void TheMenu_OffersWhatTheTileCanDo()
    {
        Seed();
        var vm = Create();
        vm.Refresh();

        var entries = vm.BuildContextMenu(Tiles(vm).Single())!;

        Assert.Equal(new[] { "Request this issue", "Follow this series", "Hide from the list" }, entries.Select(e => e.Header));
        Assert.Null(vm.BuildContextMenu("something else"));
    }
}
