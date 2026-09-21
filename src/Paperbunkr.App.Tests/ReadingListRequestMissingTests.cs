using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// "Request missing" on reading lists (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §8). Same isolation as
/// <see cref="ReadingScreenViewModelTests"/>: the screen has no context-factory seam, so <see cref="PaperbunkrDbContext.DatabasePathOverride"/>
/// points at a temp database.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadingListRequestMissingTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly List<ActivityRun> _runs = new();
    private readonly FakeComicVine _comicVine = new();

    public ReadingListRequestMissingTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reading_request_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private sealed class FakeComicVine : IComicVineClient
    {
        public List<ComicVineVolume> Volumes { get; } = new();
        public Dictionary<int, List<ComicVineIssue>> Issues { get; } = new();
        public Exception? Throw { get; set; }

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken)
        {
            if (Throw is not null) throw Throw;
            return Task.FromResult<IReadOnlyList<ComicVineVolume>>(Volumes.ToList());
        }

        public Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken) => Task.FromResult(Volumes.FirstOrDefault(v => v.Id == volumeId));

        public Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ComicVineIssue>>(Issues.TryGetValue(volumeId, out var l) ? l : new List<ComicVineIssue>());
    }

    private ReadingScreenViewModel Create() => new(new FakeFilePicker(), (_, _) => { }, activity: new ActivityService(dispatch: a => a(), recordRun: _runs.Add), loadOnConstruction: false)
    {
        CreateComicVine = _ => _comicVine,
    };

    private static void SetKey()
    {
        using var context = PaperbunkrDb.CreateContext();
        CredentialStore.Set(context, "ComicVine", CredentialKind.ApiKey, "CV");
    }

    private int SeedList(bool arcLinked, params (string Series, string Number)[] entries)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new ReadingList
        {
            Name = "Arc", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Source = arcLinked ? "ComicVine" : null, ArcId = arcLinked ? "77" : null,
        };
        int order = 0;
        foreach (var (series, number) in entries)
        {
            var placeholder = ReadingListMatcher.ResolveOrCreatePlaceholder(context, series, number, volume: null, year: 2016, format: null);
            list.Items.Add(new ReadingListItem { IssueId = placeholder.Id, SortOrder = order++ });
        }

        context.ReadingLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }

    private void SpawnInComicVine()
    {
        _comicVine.Volumes.Add(new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null));
        _comicVine.Issues[100] = new List<ComicVineIssue>
        {
            new(1, "263", null, null, null, null, 100),
            new(2, "264", null, null, null, null, 100),
        };
    }

    [Fact]
    public async Task RequestMissing_TurnsAnArcListsPlaceholdersIntoWantedIssues_AsOneActivityJob()
    {
        SetKey();
        SpawnInComicVine();
        int listId = SeedList(arcLinked: true, ("Spawn", "263"), ("Spawn", "264"));
        var vm = Create();
        vm.LoadReadingList(listId);
        Assert.True(vm.IsArcLinked);

        await vm.RequestMissingFromArcCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(2, context.WantedIssues.Count(w => w.Status == WantedIssueStatus.Wanted));
        Assert.False(context.WatchedSeries.Single().WatchFutureReleases);     // requesting arc issues never follows the series
        Assert.Equal("Requested 2 issues.", vm.StatusMessage);

        var run = Assert.Single(_runs);
        Assert.Equal(ActivityJobKind.Acquisition, run.Kind);
        Assert.Equal(ActivityRunStatus.Succeeded, run.ResultSummary is null ? ActivityRunStatus.Failed : ActivityRunStatus.Succeeded);
        Assert.Equal(2, run.ItemsProcessed);
        Assert.Equal("WantedScreen", run.ResultLinkKind);
    }

    [Fact]
    public async Task RequestMissing_ReportsWhatCouldNotBeMatched_InsteadOfDroppingIt()
    {
        SetKey();
        SpawnInComicVine();
        int listId = SeedList(arcLinked: true, ("Spawn", "263"), ("Nonexistent Comic", "1"));
        var vm = Create();
        vm.LoadReadingList(listId);

        await vm.RequestMissingFromArcCommand.ExecuteAsync(null);

        Assert.Contains("Requested 1 issue", vm.StatusMessage);
        Assert.Contains("1 couldn't be matched", vm.StatusMessage);
        Assert.Contains("Nonexistent Comic #1", vm.StatusMessage);
    }

    [Fact]
    public async Task RequestMissing_IsRefusedOnAListThatIsNotLinkedToAnArc()
    {
        SetKey();
        SpawnInComicVine();
        int listId = SeedList(arcLinked: false, ("Spawn", "263"));
        var vm = Create();
        vm.LoadReadingList(listId);

        await vm.RequestMissingFromArcCommand.ExecuteAsync(null);

        Assert.Contains("only available on lists built from a story arc", vm.StatusMessage);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.WantedIssues);
        Assert.Empty(_runs);
    }

    [Fact]
    public async Task RequestMissing_NeedsAComicVineKey_AndSaysSo()
    {
        SpawnInComicVine();
        int listId = SeedList(arcLinked: true, ("Spawn", "263"));
        var vm = Create();
        vm.LoadReadingList(listId);

        await vm.RequestMissingFromArcCommand.ExecuteAsync(null);

        Assert.Contains("ComicVine API key", vm.StatusMessage);
        Assert.Empty(_runs);
    }

    [Fact]
    public async Task ARateLimit_FailsTheJob_WithTheReason()
    {
        SetKey();
        _comicVine.Throw = new ComicVineException("ComicVine is rate-limiting requests.", 107);
        int listId = SeedList(arcLinked: true, ("Spawn", "263"));
        var vm = Create();
        vm.LoadReadingList(listId);

        await vm.RequestMissingFromArcCommand.ExecuteAsync(null);

        Assert.Equal("ComicVine is rate-limiting requests.", vm.StatusMessage);
        Assert.Equal(ActivityRunStatus.Failed, Assert.Single(_runs).Status);
    }

    [Fact]
    public void OnlyPlaceholderRows_OfferARequest()
    {
        SetKey();
        int listId = SeedList(arcLinked: false, ("Spawn", "263"));
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "Owned" };
            context.Series.Add(series);
            context.SaveChanges();
            var owned = new Issue { SeriesId = series.Id, Number = "1", FilePath = "C:\\x\\1.cbz" };
            context.Issues.Add(owned);
            context.SaveChanges();
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = owned.Id, SortOrder = 9 });
            context.SaveChanges();
        }

        var vm = Create();
        vm.LoadReadingList(listId);

        var rows = vm.Groups.SelectMany(g => g.Rows).OrderBy(r => r.Position).ToList();
        Assert.True(rows[0].IsPlaceholder);
        Assert.True(rows[0].CanRequest);
        Assert.False(rows[1].IsPlaceholder);
        Assert.False(rows[1].CanRequest);          // a real issue in the library is never requested
    }

    [Fact]
    public async Task ARowsRequestButton_RequestsThatOneIssue_OnAnyList()
    {
        SetKey();
        SpawnInComicVine();
        int listId = SeedList(arcLinked: false, ("Spawn", "263"), ("Spawn", "264"));
        var vm = Create();
        vm.LoadReadingList(listId);
        var row = vm.Groups.SelectMany(g => g.Rows).First(r => r.Item.Issue!.Number == "264");

        row.RequestCommand.Execute(null);
        for (int i = 0; i < 100 && _runs.Count == 0; i++)
        {
            await Task.Delay(20);                  // the request runs as a background job
        }

        using var context = PaperbunkrDb.CreateContext();
        var wanted = Assert.Single(context.WantedIssues);
        Assert.Equal("264", wanted.IssueNumber);
    }

    [Fact]
    public void TheSummary_ListsAtMostThreeUnresolvedEntries_ThenCountsTheRest()
    {
        var result = new ArcRequestResult(4, 2, new[]
        {
            new UnresolvedRequest("A", "1", "no match"),
            new UnresolvedRequest("B", "2", "no match"),
            new UnresolvedRequest("C", "3", "no match"),
            new UnresolvedRequest("D", "4", "no match"),
            new UnresolvedRequest("E", "5", "no match"),
        });

        var text = ReadingScreenViewModel.DescribeRequestResult(result);

        Assert.StartsWith("Requested 4 issues, 2 already requested, 5 couldn't be matched.", text);
        Assert.Contains("A #1: no match", text);
        Assert.Contains("C #3: no match", text);
        Assert.DoesNotContain("D #4", text);
        Assert.Contains("…and 2 more.", text);
    }

    [Fact]
    public void FollowArc_PersistsOnArcLists_AndReloadsWithTheList_ButNeverOnPlainOnes()
    {
        int arcId = SeedList(arcLinked: true, ("Spawn", "263"));
        var vm = Create();
        vm.LoadReadingList(arcId);
        Assert.False(vm.FollowArc);

        vm.FollowArc = true;

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.True(context.ReadingLists.Single(l => l.Id == arcId).FollowArc);
        }

        var reopened = Create();
        reopened.LoadReadingList(arcId);
        Assert.True(reopened.FollowArc);                       // loading the list must not write it back or reset it

        int plainId = SeedList(arcLinked: false, ("Spawn", "263"));
        reopened.LoadReadingList(plainId);
        Assert.False(reopened.FollowArc);
        reopened.FollowArc = true;                              // the menu item is hidden for these; the guard is belt-and-braces
        using var check = PaperbunkrDb.CreateContext();
        Assert.False(check.ReadingLists.Single(l => l.Id == plainId).FollowArc);
    }
}
