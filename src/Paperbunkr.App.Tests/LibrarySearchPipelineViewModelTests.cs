using System.Collections.Specialized;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.LibrarySearch;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Scheduling test double: nothing runs until the test says so, so the tests can observe the state
/// <i>between</i> a keystroke and its swap. Mirrors the real scheduler's supersede rule (a newer
/// request replaces the pending one).
/// </summary>
internal sealed class ManualLibraryViewScheduler : ILibraryViewScheduler
{
    private Action? _pending;
    private readonly Dictionary<string, (TimeSpan Delay, Action Action)> _debounced = new();

    public TimeSpan? LastDebounce { get; private set; }
    public int ScheduleCount { get; private set; }
    public bool HasPending => _pending is not null;

    /// <summary>Wall time of the last pending job's compute / apply halves, for the perf harness's breakdown.</summary>
    public double LastComputeMs { get; private set; }
    public double LastApplyMs { get; private set; }

    public void Schedule<T>(TimeSpan debounce, Func<CancellationToken, T> compute, Action<T> apply)
    {
        LastDebounce = debounce;
        ScheduleCount++;
        _pending = () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = compute(CancellationToken.None);
            LastComputeMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            apply(result);
            LastApplyMs = sw.Elapsed.TotalMilliseconds;
        };
    }

    public void RunNow<T>(Func<CancellationToken, T> compute, Action<T> apply)
    {
        _pending = null;
        apply(compute(CancellationToken.None));
    }

    public void Debounce(string key, TimeSpan delay, Action action) => _debounced[key] = (delay, action);

    public void RunPending()
    {
        var pending = _pending;
        _pending = null;
        pending?.Invoke();
    }

    public bool HasDebounced(string key) => _debounced.ContainsKey(key);

    public TimeSpan DebounceDelay(string key) => _debounced[key].Delay;

    public void RunDebounced(string key)
    {
        if (_debounced.Remove(key, out var entry))
        {
            entry.Action();
        }
    }
}

/// <summary>
/// The view-model side of docs/superpowers/specs/2026-09-19-library-search-perf-design.md: per-issue search
/// matching, one swap per result, debounce policy, scroll policy, selection resync, and the in-place
/// series invalidation hook. Same temp-database setup as <see cref="LibraryScreenViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibrarySearchPipelineViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibrarySearchPipelineViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_library_pipeline_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
        }
    }

    private static int CreateSeries(string name, params (string Number, string? Writer)[] issues)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, ContentType = ContentType.Comic };
        context.Series.Add(series);
        context.SaveChanges();

        foreach (var (number, writer) in issues)
        {
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = number, Writer = writer });
        }

        context.SaveChanges();
        return series.Id;
    }

    private static LibraryScreenViewModel NewViewModel() =>
        new(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

    private static LibraryScreenViewModel NewViewModel(ManualLibraryViewScheduler scheduler)
    {
        var vm = NewViewModel();
        vm.ViewScheduler = scheduler;
        return vm;
    }

    // ------------------------------------------------------------------ per-issue search (Q16)

    [Fact]
    public void WriterSearch_ListsOnlyTheMatchingIssues_AndKeepsTheSeriesCard()
    {
        CreateSeries("Batman", ("1", "Frank Miller"), ("2", "Alan Moore"));
        CreateSeries("Saga", ("1", "Brian K. Vaughan"));
        var vm = NewViewModel();

        vm.SearchMode = SearchMode.Writer;
        vm.SearchQuery = "miller";

        Assert.Equal("Frank Miller", Assert.Single(vm.IssueList.Rows).Writer);
        Assert.Equal("Batman", Assert.Single(vm.Covers).Name);
    }

    [Fact]
    public void SeriesNameSearch_ListsEveryIssueOfThatSeries()
    {
        CreateSeries("Batman", ("1", "Frank Miller"), ("2", "Alan Moore"));
        CreateSeries("Saga", ("1", "Brian K. Vaughan"));
        var vm = NewViewModel();

        vm.SearchQuery = "batman";

        Assert.Equal(2, vm.IssueList.Rows.Count);
        Assert.All(vm.IssueList.Rows, r => Assert.Equal("Batman", r.SeriesName));
    }

    // ------------------------------------------------------------------ scheduling / single swap

    [Fact]
    public void SearchQueryChange_WaitsForTheScheduler_ThenSwapsOnce()
    {
        CreateSeries("Batman", ("1", "Frank Miller"));
        CreateSeries("Saga", ("1", "Brian K. Vaughan"));
        var scheduler = new ManualLibraryViewScheduler();
        var vm = NewViewModel(scheduler);
        var coverEvents = new List<NotifyCollectionChangedAction>();
        var rowEvents = new List<NotifyCollectionChangedAction>();
        vm.Covers.CollectionChanged += (_, e) => coverEvents.Add(e.Action);
        vm.IssueList.Rows.CollectionChanged += (_, e) => rowEvents.Add(e.Action);

        vm.SearchQuery = "batman";

        // Nothing moved yet: the keystroke handler only scheduled the work.
        Assert.Equal(2, vm.Covers.Count);
        Assert.Empty(coverEvents);
        Assert.True(scheduler.HasPending);

        scheduler.RunPending();

        Assert.Equal("Batman", Assert.Single(vm.Covers).Name);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, coverEvents);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, rowEvents);
    }

    [Fact]
    public void ANewerKeystroke_SupersedesTheOlderPendingSwap()
    {
        CreateSeries("Batman", ("1", null));
        CreateSeries("Saga", ("1", null));
        var scheduler = new ManualLibraryViewScheduler();
        var vm = NewViewModel(scheduler);

        vm.SearchQuery = "bat";
        vm.SearchQuery = "saga";
        scheduler.RunPending();

        Assert.Equal("Saga", Assert.Single(vm.Covers).Name);
        Assert.False(scheduler.HasPending);
    }

    [Fact]
    public void SearchText_IsDebounced150ms_ButClearingTheBoxIsImmediate()
    {
        CreateSeries("Batman", ("1", null));
        var scheduler = new ManualLibraryViewScheduler();
        var vm = NewViewModel(scheduler);

        vm.SearchQuery = "bat";
        Assert.Equal(TimeSpan.FromMilliseconds(150), scheduler.LastDebounce);

        vm.SearchQuery = string.Empty;
        Assert.Equal(TimeSpan.Zero, scheduler.LastDebounce);

        vm.SearchQuery = "  ";
        Assert.Equal(TimeSpan.Zero, scheduler.LastDebounce);

        vm.SearchQuery = "writer:";
        Assert.Equal(TimeSpan.Zero, scheduler.LastDebounce);
    }

    [Fact]
    public void SearchSettingsWrite_IsDebouncedOffTheKeystrokePath()
    {
        CreateSeries("Batman", ("1", null));
        var scheduler = new ManualLibraryViewScheduler();
        var vm = NewViewModel(scheduler);

        vm.SearchQuery = "batman";

        Assert.Null(ReadSettings().LibrarySearchQuery);
        Assert.True(scheduler.HasDebounced("library-search-save"));
        Assert.Equal(TimeSpan.FromMilliseconds(500), scheduler.DebounceDelay("library-search-save"));

        scheduler.RunDebounced("library-search-save");

        Assert.Equal("batman", ReadSettings().LibrarySearchQuery);
    }

    private static AppSettings ReadSettings()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.GetOrCreateAppSettings();
    }

    // ------------------------------------------------------------------ scroll policy

    [Fact]
    public void ScrollToTop_IsRaisedForSearchAndScope_ButNotForSortGroupOrFilter()
    {
        CreateSeries("Batman", ("1", "Frank Miller"));
        var vm = NewViewModel();
        int scrolls = 0;
        vm.ScrollToTopRequested += () => scrolls++;

        vm.SearchQuery = "batman";
        Assert.Equal(1, scrolls);

        vm.SearchMode = SearchMode.Series;
        Assert.Equal(2, scrolls);

        vm.IssueList.GroupField = IssueListGroupField.Publisher;
        vm.IssueList.SortField = IssueListSortField.Series;
        vm.FilterUnreadOnly = true;
        Assert.Equal(2, scrolls);
    }

    // ------------------------------------------------------------------ one-shot entrance pulse

    [Fact]
    public void ViewModeChange_PulsesTheEntranceFlag_FalseThenTrue()
    {
        CreateSeries("Batman", ("1", null));
        var vm = NewViewModel();
        var seen = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryScreenViewModel.PlayEntranceAnimation))
            {
                seen.Add(vm.PlayEntranceAnimation);
            }
        };

        vm.ViewMode = LibraryViewMode.List;

        Assert.Equal(new[] { false, true }, seen);
    }

    [Fact]
    public void SortGroupSwap_PulsesTheEntranceFlag_ButASearchSwapLeavesItFalse()
    {
        CreateSeries("Batman", ("1", null));
        var vm = NewViewModel();
        var seen = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryScreenViewModel.PlayEntranceAnimation))
            {
                seen.Add(vm.PlayEntranceAnimation);
            }
        };

        vm.IssueList.GroupField = IssueListGroupField.Publisher;
        Assert.Equal(new[] { false, true }, seen);

        seen.Clear();
        vm.SearchQuery = "bat";
        Assert.Equal(new[] { false }, seen);
    }

    // ------------------------------------------------------------------ selection resync

    [Fact]
    public void SelectionCleared_WhileARowIsFilteredOut_ShowsItUnselectedWhenItReturns()
    {
        CreateSeries("Batman", ("1", null));
        CreateSeries("Saga", ("1", null));
        var vm = NewViewModel();

        var batmanRow = vm.IssueList.Rows.Single(r => r.SeriesName == "Batman");
        vm.ToggleIssueSelection(batmanRow, isShiftHeld: false);
        Assert.True(batmanRow.IsSelected);

        vm.SearchQuery = "saga";                       // Batman leaves the list, cached row keeps IsSelected = true
        vm.ClearSelectionCommand.Execute(null);        // only clears the rows currently displayed
        vm.SearchQuery = string.Empty;                 // Batman returns

        var returned = vm.IssueList.Rows.Single(r => r.SeriesName == "Batman");
        Assert.False(returned.IsSelected);
        Assert.Equal(0, vm.Selection.Count);
    }

    [Fact]
    public void SelectionMadeWhileFiltered_IsShownOnTheRowsAfterTheFilterIsCleared()
    {
        CreateSeries("Batman", ("1", null));
        CreateSeries("Saga", ("1", null));
        var vm = NewViewModel();

        vm.SearchQuery = "saga";
        vm.ToggleIssueSelection(Assert.Single(vm.IssueList.Rows), isShiftHeld: false);
        vm.SearchQuery = string.Empty;

        Assert.True(vm.IssueList.Rows.Single(r => r.SeriesName == "Saga").IsSelected);
        Assert.False(vm.IssueList.Rows.Single(r => r.SeriesName == "Batman").IsSelected);
    }

    [Fact]
    public void Swap_WritesNoIsSelected_WhenNothingDiffers()
    {
        CreateSeries("Batman", ("1", null));
        CreateSeries("Saga", ("1", null));
        var vm = NewViewModel();
        var changes = new List<string?>();
        foreach (var row in vm.IssueList.Rows)
        {
            row.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        }

        vm.SearchQuery = "a";
        vm.SearchQuery = string.Empty;

        Assert.DoesNotContain(nameof(Paperbunkr.App.Models.IssueListRow.IsSelected), changes);
    }

    // ------------------------------------------------------------------ in-place series invalidation

    [Fact]
    public void SetSeriesStatus_UpdatesTheCardWithoutAReload()
    {
        int seriesId = CreateSeries("Batman", ("1", null));
        var vm = NewViewModel();

        vm.SetSeriesStatusOngoingCommand.Execute(seriesId);

        Assert.Equal("Ongoing", Assert.Single(vm.Covers).SeriesStatusLabel);
        Assert.Equal("Ongoing", Assert.Single(vm.IssueList.Rows).SeriesStatusLabel);
    }

    [Fact]
    public void SetSeriesReadingStatus_UpdatesTheCardWithoutAReload()
    {
        int seriesId = CreateSeries("Batman", ("1", null));
        var vm = NewViewModel();

        vm.SetSeriesReadingStatusCompletedCommand.Execute(seriesId);

        Assert.Equal("Completed", Assert.Single(vm.Covers).ReadingStatusLabel);
    }

    [Fact]
    public void SetSeriesReadingMode_UpdatesTheCardWithoutAReload()
    {
        int seriesId = CreateSeries("Batman", ("1", null));
        var vm = NewViewModel();

        // Both directions, so the assertion cannot pass just because one of them is the default.
        vm.SetSeriesReadingModeLeftToRightCommand.Execute(seriesId);
        Assert.Equal("LeftToRight", Assert.Single(vm.Covers).ReadingDirectionLabel);

        vm.SetSeriesReadingModeRightToLeftCommand.Execute(seriesId);
        Assert.Equal("RightToLeft", Assert.Single(vm.Covers).ReadingDirectionLabel);
    }

    [Fact]
    public void Invalidation_WhileASearchIsPending_SupersedesItAndStillHonoursTheSearch()
    {
        int batmanId = CreateSeries("Batman", ("1", null));
        CreateSeries("Saga", ("1", null));
        var scheduler = new ManualLibraryViewScheduler();
        var vm = NewViewModel(scheduler);

        vm.SearchQuery = "batman";                       // pending, not applied yet
        vm.SetSeriesStatusOngoingCommand.Execute(batmanId);
        scheduler.RunPending();                          // whatever is still pending must not undo the patch

        var card = Assert.Single(vm.Covers);
        Assert.Equal("Batman", card.Name);
        Assert.Equal("Ongoing", card.SeriesStatusLabel);
    }

    [Fact]
    public void LoadFromDatabase_AfterADataChange_ShowsTheNewData()
    {
        CreateSeries("Batman", ("1", null));
        var vm = NewViewModel();
        Assert.Single(vm.Covers);

        CreateSeries("Saga", ("1", null));
        vm.LoadFromDatabase();

        Assert.Equal(2, vm.Covers.Count);
        Assert.Equal(2, vm.IssueList.Rows.Count);
    }
}
