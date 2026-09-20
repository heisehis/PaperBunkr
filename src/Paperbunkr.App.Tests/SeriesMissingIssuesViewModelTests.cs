using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>The series Detail screen's "Missing Issues (n)" section. Joins <see cref="AvaloniaTestCollection"/> like every Detail test.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SeriesMissingIssuesViewModelTests : IDisposable
{
    private static readonly DateTime Today = DateTime.Today;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_missing_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly FakeComicVine _comicVine = new();
    private readonly int _seriesId;

    public SeriesMissingIssuesViewModelTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath};Foreign Keys=True").Options;
        using var context = NewContext();
        context.Database.EnsureCreated();
        var series = new Series { Name = "Spawn" };
        context.Series.Add(series);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = series.Id, Number = "261", FilePath = "C:\\x\\261.cbz" });
        context.SaveChanges();
        _seriesId = series.Id;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext() => new(_options);

    private SeriesMissingIssuesViewModel Create() => new(NewContext, _ => _comicVine, post: a => a());

    private sealed class FakeComicVine : IComicVineClient
    {
        public List<ComicVineVolume> Volumes { get; } = new();
        public List<ComicVineIssue> Issues { get; } = new();
        public Exception? Throw { get; set; }
        public int IssueCalls { get; private set; }

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken)
        {
            if (Throw is not null) throw Throw;
            return Task.FromResult<IReadOnlyList<ComicVineVolume>>(Volumes.ToList());
        }

        public Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken) => Task.FromResult(Volumes.FirstOrDefault(v => v.Id == volumeId));

        public Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken)
        {
            IssueCalls++;
            if (Throw is not null) throw Throw;
            return Task.FromResult<IReadOnlyList<ComicVineIssue>>(Issues.ToList());
        }
    }

    private void SetKey()
    {
        using var context = NewContext();
        CredentialStore.Set(context, "ComicVine", CredentialKind.ApiKey, "CV");
    }

    /// <summary>Tracks volume 100 for the local series with issues 261 (owned), 262 (past), 263 (upcoming) in its catalog.</summary>
    private void TrackWithCatalog()
    {
        using var context = NewContext();
        var watched = WantedService.TrackVolume(context, new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null), _seriesId, watchFutureReleases: false);
        WantedService.RefreshCatalog(context, watched, new[]
        {
            new ComicVineIssue(1, "261", "Owned", Today.AddDays(-30), null, null, 100),
            new ComicVineIssue(2, "262", "Past", Today.AddDays(-7), null, null, 100),
            new ComicVineIssue(3, "263", null, Today.AddDays(14), null, null, 100),
        });
    }

    [Fact]
    public void AnUntrackedSeries_OffersToFindItsComicVineListing_AndShowsNoMissingIssues()
    {
        var vm = Create();

        vm.Load(_seriesId, "Spawn");

        Assert.True(vm.IsNotTracked);
        Assert.False(vm.IsTracked);
        Assert.Empty(vm.MissingRows);
        Assert.False(vm.IsAllCaughtUp);
    }

    [Fact]
    public void ATrackedSeries_ListsWhatItLacks_ExcludingOwnedIssues_WithTheCountInTheHeading()
    {
        TrackWithCatalog();
        var vm = Create();

        vm.Load(_seriesId, "Spawn");

        Assert.True(vm.IsTracked);
        Assert.Equal(new[] { "262", "263" }, vm.MissingRows.Select(r => r.Number));   // 261 is owned
        Assert.Equal("Missing Issues (2)", vm.Heading);
        Assert.False(vm.MissingRows[0].IsUpcoming);
        Assert.True(vm.MissingRows[1].IsUpcoming);
        Assert.Equal("#262 · Past", vm.MissingRows[0].Title);
        Assert.Equal("Spawn #263", vm.MissingRows[1].Title);                            // no issue name: falls back to the series name
    }

    [Fact]
    public void Request_MarksTheIssueWanted_AndItLeavesTheMissingList()
    {
        TrackWithCatalog();
        var vm = Create();
        vm.Load(_seriesId, "Spawn");

        vm.RequestCommand.Execute(vm.MissingRows[0]);

        Assert.Equal(new[] { "263" }, vm.MissingRows.Select(r => r.Number));
        Assert.Equal("Missing Issues (1)", vm.Heading);
        using var context = NewContext();
        var wanted = Assert.Single(context.WantedIssues);
        Assert.Equal("262", wanted.IssueNumber);
        Assert.Equal(WantedIssueStatus.Wanted, wanted.Status);
        Assert.Contains("Requested #262", vm.StatusMessage);
    }

    [Fact]
    public void RequestAll_AsksFirst_AndOnlyRequestsOnceConfirmed()
    {
        TrackWithCatalog();
        var vm = Create();
        vm.Load(_seriesId, "Spawn");

        vm.RequestAllCommand.Execute(null);

        Assert.True(vm.IsConfirmingRequestAll);
        Assert.Equal("Request all 2 issues?", vm.ConfirmText);
        using (var context = NewContext())
        {
            Assert.Empty(context.WantedIssues);                 // asking is not requesting
        }

        vm.CancelRequestAllCommand.Execute(null);
        Assert.False(vm.IsConfirmingRequestAll);
        Assert.Equal(2, vm.MissingRows.Count);

        vm.RequestAllCommand.Execute(null);
        vm.ConfirmRequestAllCommand.Execute(null);

        Assert.False(vm.IsConfirmingRequestAll);
        Assert.Empty(vm.MissingRows);
        Assert.True(vm.IsAllCaughtUp);
        using var check = NewContext();
        Assert.Equal(2, check.WantedIssues.Count());
        Assert.Equal("Requested 2 issues.", vm.StatusMessage);
    }

    [Fact]
    public void RequestAll_DoesNothing_WhenNothingIsMissing()
    {
        TrackWithCatalog();
        var vm = Create();
        vm.Load(_seriesId, "Spawn");
        vm.RequestAllCommand.Execute(null);
        vm.ConfirmRequestAllCommand.Execute(null);

        vm.RequestAllCommand.Execute(null);                       // nothing left

        Assert.False(vm.IsConfirmingRequestAll);
    }

    [Fact]
    public void IHaveThis_RemovesTheIssueFromMissing_WithoutRequestingIt()
    {
        TrackWithCatalog();
        var vm = Create();
        vm.Load(_seriesId, "Spawn");

        vm.IgnoreCommand.Execute(vm.MissingRows[0]);

        Assert.Equal(new[] { "263" }, vm.MissingRows.Select(r => r.Number));
        using var context = NewContext();
        Assert.Equal(WantedIssueStatus.Ignored, Assert.Single(context.WantedIssues).Status);
    }

    [Fact]
    public void FollowToggle_Persists_ButLoadingNeverFiresIt()
    {
        TrackWithCatalog();
        var vm = Create();
        vm.Load(_seriesId, "Spawn");
        vm.Load(_seriesId, "Spawn");                              // reloading must not flip anything
        using (var context = NewContext())
        {
            Assert.False(context.WatchedSeries.Single().WatchFutureReleases);
        }

        vm.WatchFutureReleases = true;

        using var check = NewContext();
        Assert.True(check.WatchedSeries.Single().WatchFutureReleases);
        Assert.Contains("Following", vm.StatusMessage);
    }

    [Fact]
    public async Task FindOnComicVine_NeedsAKey_ThenListsMatchingVolumes()
    {
        var vm = Create();
        vm.Load(_seriesId, "Spawn");

        await vm.FindOnComicVineCommand.ExecuteAsync(null);
        Assert.True(vm.HasErrorStatus);
        Assert.Empty(vm.SearchResults);

        SetKey();
        _comicVine.Volumes.Add(new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null));
        vm.Load(_seriesId, "Spawn");
        await vm.FindOnComicVineCommand.ExecuteAsync(null);

        Assert.Single(vm.SearchResults);
        Assert.True(vm.HasSearchResults);
        Assert.True(vm.HasComicVineKey);
    }

    [Fact]
    public async Task Track_LinksTheVolumeToThisSeries_CachesTheCatalog_AndShowsWhatIsMissing()
    {
        SetKey();
        _comicVine.Issues.AddRange(new[]
        {
            new ComicVineIssue(1, "261", null, Today.AddDays(-30), null, null, 100),
            new ComicVineIssue(2, "262", null, Today.AddDays(-7), null, null, 100),
        });
        var vm = Create();
        vm.Load(_seriesId, "Spawn");

        await vm.TrackCommand.ExecuteAsync(new VolumeResultViewModel { Volume = new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null) });

        using var context = NewContext();
        var watched = context.WatchedSeries.Single();
        Assert.Equal(_seriesId, watched.SeriesId);
        Assert.False(watched.WatchFutureReleases);                 // tracking is not following
        Assert.Equal(2, context.CatalogIssues.Count());
        Assert.True(vm.IsTracked);
        Assert.Equal(new[] { "262" }, vm.MissingRows.Select(r => r.Number));   // 261 is owned
        Assert.Empty(vm.SearchResults);
    }

    [Fact]
    public async Task Track_RefusesAVolumeAlreadyLinkedToAnotherLocalSeries()
    {
        SetKey();
        int otherSeriesId;
        using (var context = NewContext())
        {
            var other = new Series { Name = "Spawn (2nd copy)" };
            context.Series.Add(other);
            context.SaveChanges();
            otherSeriesId = other.Id;
            WantedService.TrackVolume(context, new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null), otherSeriesId, false);
        }

        var vm = Create();
        vm.Load(_seriesId, "Spawn");

        await vm.TrackCommand.ExecuteAsync(new VolumeResultViewModel { Volume = new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null) });

        Assert.True(vm.HasErrorStatus);
        Assert.False(vm.IsTracked);
        using var check = NewContext();
        Assert.Equal(otherSeriesId, check.WatchedSeries.Single().SeriesId);       // the existing link is untouched
    }

    [Fact]
    public async Task RefreshFromComicVine_PicksUpNewIssues()
    {
        SetKey();
        TrackWithCatalog();
        _comicVine.Issues.AddRange(new[]
        {
            new ComicVineIssue(1, "261", null, Today.AddDays(-30), null, null, 100),
            new ComicVineIssue(2, "262", null, Today.AddDays(-7), null, null, 100),
            new ComicVineIssue(3, "263", null, Today.AddDays(14), null, null, 100),
            new ComicVineIssue(4, "264", null, Today.AddDays(21), null, null, 100),
        });
        var vm = Create();
        vm.Load(_seriesId, "Spawn");
        Assert.Equal(2, vm.MissingRows.Count);

        await vm.RefreshFromComicVineCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.MissingRows.Count);
        Assert.Contains("Updated from ComicVine", vm.StatusMessage);
    }

    [Fact]
    public async Task ComicVineFailures_BecomeStatusMessages()
    {
        SetKey();
        _comicVine.Throw = new ComicVineException("ComicVine request failed.");
        var vm = Create();
        vm.Load(_seriesId, "Spawn");

        await vm.FindOnComicVineCommand.ExecuteAsync(null);

        Assert.True(vm.HasErrorStatus);
        Assert.Equal("ComicVine request failed.", vm.StatusMessage);
    }

    [Fact]
    public void LoadingADifferentSeries_ResetsTheSection()
    {
        TrackWithCatalog();
        var vm = Create();
        vm.Load(_seriesId, "Spawn");
        vm.RequestAllCommand.Execute(null);
        Assert.True(vm.IsConfirmingRequestAll);

        vm.Load(_seriesId + 999, "Something else");

        Assert.False(vm.IsConfirmingRequestAll);
        Assert.True(vm.IsNotTracked);
        Assert.Empty(vm.MissingRows);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [Fact]
    public void TheDetailTabs_LoadTheMissingSection_WithTheSeries()
    {
        TrackWithCatalog();
        var tabs = new DetailTabsViewModel(_ => { }, _ => { }, null, NewContext);
        Series series;
        using (var context = NewContext())
        {
            series = context.Series.Include(s => s.Issues).Single(s => s.Id == _seriesId);
        }

        tabs.LoadSeries(series);

        Assert.True(tabs.Missing.IsTracked);
        Assert.Equal(new[] { "262", "263" }, tabs.Missing.MissingRows.Select(r => r.Number));
    }

    [Fact]
    public void TheMangaHost_DoesNotLoadTheSection()
    {
        TrackWithCatalog();
        var tabs = new DetailTabsViewModel(_ => { }, _ => { }, null, NewContext) { IsMangaDetailHost = true };
        Series series;
        using (var context = NewContext())
        {
            series = context.Series.Include(s => s.Issues).Single(s => s.Id == _seriesId);
        }

        tabs.LoadSeries(series);

        Assert.False(tabs.Missing.IsTracked);
    }

    /// <summary>Proves the section's compiled XAML was woven (see CLAUDE.md, "adding a new Avalonia View").</summary>
    [Fact]
    public void TheSectionView_Constructs_AndBindsToTheViewModel()
    {
        TestAppBuilder.EnsureInitialized();
        TrackWithCatalog();
        var vm = Create();
        vm.Load(_seriesId, "Spawn");

        var view = new SeriesMissingIssuesView { DataContext = vm };

        Assert.NotNull(view.Content);
    }
}
