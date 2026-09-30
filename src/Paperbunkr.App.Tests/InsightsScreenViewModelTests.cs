using System;
using System.Threading.Tasks;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="InsightsScreenViewModel"/> - the session cache and event-driven
/// invalidation (docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md §5). The tile
/// computation itself is covered by <c>InsightsResolverTests</c> (Paperbunkr.Data.Tests).
/// </summary>
public class InsightsScreenViewModelTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public InsightsScreenViewModelTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_insights_vm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static InsightsScreenViewModel NewVm(IReadingEventRecorder? recorder = null, Func<DateTime>? nowUtc = null)
        => new(_ => { }, _ => { }, _ => { }, () => { }, new FakeDialogService(), recorder, nowUtc ?? (() => new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc)));

    private sealed class FakeDialogService : IDialogService
    {
        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(0);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(true);
    }

    [Fact]
    public void Refresh_OnEmptyLibrary_DoesNotThrow()
    {
        var vm = NewVm();
        vm.Refresh();

        Assert.NotNull(vm.Snapshot);
        Assert.Equal(0, vm.ContinueCount);
        Assert.True(vm.ReadingAllClear);
        Assert.False(vm.HasRecommendations);
        Assert.Empty(vm.Recommendations);
    }

    [Fact]
    public void Refresh_WithFinishedSeriesAndRealRelation_PopulatesRecommendations()
    {
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            var source = new Series { Name = "Source Series" };
            var target = new Series { Name = "Target Series" };
            ctx.Series.Add(source);
            ctx.Series.Add(target);
            ctx.SaveChanges();

            var issue = new Issue { SeriesId = source.Id };
            ctx.Issues.Add(issue);
            ctx.SaveChanges();
            ctx.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Comic,
                ItemId = issue.Id,
                Kind = ReadingEventKind.Finished,
                TimestampUtc = DateTime.UtcNow,
                SeriesId = source.Id,
            });
            ctx.SaveChanges();

            MediaRelationResolver.TryCreate(ctx, source.Id, target.Id, RelationType.Prequel);
        }

        var vm = NewVm();
        vm.Refresh();

        Assert.True(vm.HasRecommendations);
        Assert.Equal("Source Series", vm.RecommendationsSeedName);
        var card = Assert.Single(vm.Recommendations);
        Assert.Equal("Target Series", card.Card.Name);
        Assert.NotEmpty(card.Explanation);
    }

    [Fact]
    public void Refresh_ServesFromCache_UntilInvalidated()
    {
        var vm = NewVm();
        vm.Refresh();
        var first = vm.Snapshot;

        vm.Refresh();
        Assert.Same(first, vm.Snapshot); // no recorder event fired - still cached
    }

    [Fact]
    public void ReadingEvent_WhileActive_InvalidatesCacheAndRebuilds()
    {
        var recorder = new FakeRecorder();
        var vm = NewVm(recorder);
        vm.IsActive = true;
        vm.Refresh();
        var before = vm.Snapshot;

        // A new in-progress issue lands, which Continue should now pick up.
        int seriesId;
        int issueId;
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "New" };
            ctx.Series.Add(series);
            ctx.SaveChanges();
            var issue = new Issue { SeriesId = series.Id, PageCount = 100, LastPageRead = 50 };
            ctx.Issues.Add(issue);
            ctx.SaveChanges();
            seriesId = series.Id;
            issueId = issue.Id;

            ctx.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Comic,
                ItemId = issueId,
                Kind = ReadingEventKind.Opened,
                TimestampUtc = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc),
                SeriesId = seriesId,
            });
            ctx.SaveChanges();
        }

        recorder.Raise();

        Assert.NotSame(before, vm.Snapshot);
        Assert.Equal(1, vm.ContinueCount);
        // CoverKey (docs/superpowers/specs/2026-09-23-insights-redesign-design.md's global cover rule)
        // is just the cover issue's own id per CoverFingerprint.Stem - non-null whenever the series has
        // at least one issue, which every Continue-row series does by construction.
        Assert.Equal(issueId.ToString(), vm.ContinueRows[0].CoverKey);
    }

    [Fact]
    public void PopulateLists_GapRow_ResolvesCoverKey()
    {
        int seriesId;
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "Gap Series" };
            ctx.Series.Add(series);
            ctx.SaveChanges();
            seriesId = series.Id;

            // #1, #2, #4 - a real run (InsightsResolver.ComputeGaps needs >= 3 numeric issues) missing
            // #3, at 75% ownership (exactly InsightsResolver.GapOwnershipFloor). Neither issue has its
            // file identity set, which CoverFingerprint.Stem ignores anyway (keyed purely by Issue.Id).
            ctx.Issues.Add(new Issue { SeriesId = seriesId, Number = "1" });
            ctx.Issues.Add(new Issue { SeriesId = seriesId, Number = "2" });
            ctx.Issues.Add(new Issue { SeriesId = seriesId, Number = "4" });
            ctx.SaveChanges();
        }

        var vm = NewVm();
        vm.Refresh();

        var gap = Assert.Single(vm.GapRows);
        Assert.NotNull(gap.CoverKey);
    }

    [Theory]
    [InlineData(2026, 12, 28, false)]
    [InlineData(2026, 12, 29, true)]
    [InlineData(2026, 12, 30, true)]
    [InlineData(2026, 12, 31, true)]
    [InlineData(2027, 1, 1, false)]
    public void IsWithinYearEndWindow_OnlyTrueForTheLastThreeDaysOfDecember(int year, int month, int day, bool expected)
    {
        var nowUtc = new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, InsightsScreenViewModel.IsWithinYearEndWindow(nowUtc));
    }

    [Fact]
    public void SelectRecapTab_OutsideYearEndWindow_IsGuarded()
    {
        var vm = NewVm(nowUtc: () => new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc));

        Assert.False(vm.IsRecapAvailable);
        vm.SelectRecapTabCommand.Execute(null);
        Assert.False(vm.IsRecapTabSelected);
    }

    [Fact]
    public void SelectRecapTab_WithinYearEndWindow_Works()
    {
        var vm = NewVm(nowUtc: () => new DateTime(2026, 12, 30, 12, 0, 0, DateTimeKind.Utc));

        Assert.True(vm.IsRecapAvailable);
        vm.SelectRecapTabCommand.Execute(null);
        Assert.True(vm.IsRecapTabSelected);
    }

    [Fact]
    public void Tabs_AreMutuallyExclusive_IncludingHistory()
    {
        var history = new HistoryTabViewModel(_ => { }, _ => { }, (_, _) => { }, _ => { }, new FakeDialogService(),
            runInBackground: work => Task.FromResult(work()), post: a => a());
        var vm = new InsightsScreenViewModel(_ => { }, _ => { }, _ => { }, () => { }, new FakeDialogService(),
            nowUtc: () => new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc), history: history);

        vm.SelectHistoryTabCommand.Execute(null);
        Assert.True(vm.IsHistoryTabSelected);
        Assert.False(vm.IsTodayTabSelected);
        Assert.True(history.IsActive);
        Assert.True(history.IsLoaded);

        vm.SelectTrendsTabCommand.Execute(null);
        Assert.False(vm.IsHistoryTabSelected);
        Assert.False(history.IsActive);
        Assert.True(vm.IsTrendsTabSelected);

        vm.SelectHistoryTabCommand.Execute(null);
        Assert.False(vm.IsTrendsTabSelected);

        vm.SelectTodayTabCommand.Execute(null);
        Assert.False(vm.IsHistoryTabSelected);
        Assert.True(vm.IsTodayTabSelected);
    }

    private sealed class FakeRecorder : IReadingEventRecorder
    {
        public event Action? ReadingEventRecorded;

        public void Raise() => ReadingEventRecorded?.Invoke();

        public void RecordOpened(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre) { }

        public void RecordFinished(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre, int? pagesRead) { }

        public void UpdateSessionPages(ReadingItemType itemType, int itemId, int pagesRead) { }
    }
}
