using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The five Insights-pitch cards' view-model wiring (docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md): Trends
/// state on <see cref="StatsScreenViewModel"/>, the milestones strip on <see cref="HistoryTabViewModel"/>, and <see cref="InsightsPreferences"/>.
/// The numbers themselves are covered by the resolver tests in Paperbunkr.Data.Tests.
/// </summary>
public class InsightsPitchCompletionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private readonly string? _originalOverride;
    private readonly string _dbPath;
    private readonly string _prefsPath;

    public InsightsPitchCompletionTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_insights_pitch_{Guid.NewGuid():N}.db");
        _prefsPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_insights_prefs_{Guid.NewGuid():N}.json");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _prefsPath })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private static StatsScreenViewModel NewStats(Action? openDuplicateReview = null)
        => new(_ => { }, null, () => Now) { RunSynchronously = true, OpenDuplicateReview = openDuplicateReview };

    private static int SeedIssue(string series, string number, long? size, string path)
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var s = ctx.Series.FirstOrDefault(x => x.Name == series);
        if (s is null)
        {
            s = new Series { Name = series };
            ctx.Series.Add(s);
            ctx.SaveChanges();
        }

        var issue = new Issue { SeriesId = s.Id, Number = number, FileSize = size, FilePath = path, PageCount = 20 };
        ctx.Issues.Add(issue);
        ctx.SaveChanges();
        return issue.Id;
    }

    // --- Trends ---------------------------------------------------------------------------------

    [Fact]
    public void EmptyLibrary_RhythmAndLazyCardsAreEmptyNotBroken()
    {
        var vm = NewStats();
        vm.Refresh();
        vm.SetTrendsGroupCommand.Execute(TrendsGroup.Composition);
        vm.SetTrendsGroupCommand.Execute(TrendsGroup.TopLists);

        Assert.False(vm.HasRhythmChart);
        Assert.True(vm.RhythmIsEmpty);
        Assert.False(vm.HasDropOff);
        Assert.False(vm.HasStorage);
        Assert.False(vm.HasAffinity);
        Assert.True(vm.AffinityRowsEmpty);
        Assert.False(vm.CanReviewDuplicates);
    }

    [Fact]
    public void CompositionGroup_LoadsStorageTotalsAndDuplicateSavings()
    {
        SeedIssue("Saga", "1", 40L << 20, @"C:\c\a.cbz");
        SeedIssue("Saga", "1", 10L << 20, @"C:\c\b.cbz");   // duplicate of #1
        SeedIssue("Saga", "2", 5L << 20, @"C:\c\c.cbz");
        int reviews = 0;
        var vm = NewStats(() => reviews++);

        vm.Refresh();
        Assert.False(vm.HasStorage); // lazy: nothing loads until Composition is shown

        vm.SetTrendsGroupCommand.Execute(TrendsGroup.Composition);

        Assert.True(vm.HasStorage);
        Assert.Equal("55 MB", vm.StorageTotalText);
        Assert.True(vm.HasReclaimable);
        Assert.True(vm.CanReviewDuplicates);
        Assert.Contains("10 MB", vm.ReclaimableText);

        vm.ReviewDuplicatesCommand.Execute(null);
        Assert.Equal(1, reviews);
    }

    [Fact]
    public void ReviewDuplicates_IsHiddenWhenNoNavigationCallbackIsProvided()
    {
        SeedIssue("Saga", "1", 40L << 20, @"C:\c\a.cbz");
        SeedIssue("Saga", "1", 10L << 20, @"C:\c\b.cbz");
        var vm = NewStats(openDuplicateReview: null);

        vm.SetTrendsGroupCommand.Execute(TrendsGroup.Composition);

        Assert.True(vm.HasReclaimable);
        Assert.False(vm.CanReviewDuplicates);
    }

    [Fact]
    public void ReadingActivityDoesNotReloadStorage_ButInvalidateStorageDoes()
    {
        SeedIssue("Saga", "1", 5L << 20, @"C:\c\a.cbz");
        var recorder = new FakeRecorder();
        var vm = new StatsScreenViewModel(_ => { }, recorder, () => Now) { RunSynchronously = true };
        vm.SetTrendsGroupCommand.Execute(TrendsGroup.Composition);
        Assert.Equal("5 MB", vm.StorageTotalText);

        SeedIssue("Saga", "2", 20L << 20, @"C:\c\b.cbz");
        recorder.Raise();
        vm.Refresh();
        Assert.Equal("5 MB", vm.StorageTotalText); // a read does not change what is on disk

        vm.InvalidateStorage();
        vm.Refresh();
        Assert.Equal("25 MB", vm.StorageTotalText);
    }

    [Fact]
    public void DimensionChips_SwitchTheRowsShown()
    {
        var vm = NewStats();
        vm.DropOff = new DropOffBreakdown(
            new[] { new DropOffRate("Marvel", 6, 4) },
            new[] { new DropOffRate("Horror", 2, 8) },
            Array.Empty<DropOffRate>(),
            Array.Empty<SessionLengthBucket>(),
            8, 12);

        Assert.Equal("Marvel", Assert.Single(vm.DropOffRows).Label);
        Assert.Equal("60% stopped · 6 of 10", vm.DropOffRows[0].Text);

        vm.SetDropOffDimensionCommand.Execute(DropOffDimension.Genre);
        Assert.Equal("Horror", Assert.Single(vm.DropOffRows).Label);
        Assert.True(vm.DropOffDimensionOptions.Single(o => o.Value == DropOffDimension.Genre).IsActive);
        Assert.False(vm.DropOffDimensionOptions.Single(o => o.Value == DropOffDimension.Publisher).IsActive);

        vm.Affinity = new AffinityData(
            new[] { new AffinityEntry("Brian K. Vaughan", 4.2, 4.6, 0.92, 5, 40) }, Array.Empty<AffinityEntry>(), Array.Empty<AffinityEntry>());
        Assert.Equal("Brian K. Vaughan", Assert.Single(vm.AffinityRows).Name);
        Assert.Equal("★ 4.6 · 92% read · 5 series", vm.AffinityRows[0].Text);
        vm.SetAffinityDimensionCommand.Execute(AffinityDimension.Artists);
        Assert.True(vm.AffinityRowsEmpty);
    }

    [Fact]
    public void RhythmCard_DescribesPeakAndTypicalSession()
    {
        var grid = new int[7, 24];
        grid[(int)DayOfWeek.Tuesday, 20] = 9;
        var vm = NewStats();
        vm.Rhythm = new RhythmData(grid, 9, 3, Array.Empty<SessionLengthBucket>(), 22.4, (int)DayOfWeek.Tuesday, 20);

        Assert.True(vm.HasRhythmChart);
        Assert.Equal("Most reading starts Tuesday evenings", vm.RhythmPeakText);
        Assert.Equal("Typical session 22 min", vm.RhythmTypicalText);
        Assert.Equal(9, vm.RhythmMax);
        Assert.Contains("3", vm.RhythmCoverageText);
    }

    // --- Milestones strip -----------------------------------------------------------------------

    private HistoryTabViewModel NewHistory(InsightsPreferences? prefs = null) => new(
        _ => { }, _ => { }, (_, _) => { }, _ => { }, new NoDialogs(),
        new ReadingEventRecorder(PaperbunkrDb.CreateContext),
        contextFactory: PaperbunkrDb.CreateContext,
        nowUtc: () => Now,
        timeZone: TimeZoneInfo.Utc,
        runInBackground: work => Task.FromResult(work()),
        post: a => a())
    { Preferences = prefs };

    private static void AddEvent(ReadingEventKind kind, DateTime at, int item = 1)
    {
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = item, Kind = kind, TimestampUtc = at, SeriesTitle = "Saga" });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task MilestoneStrip_ShowsEarnedAndNextUp_AndHideIsRemembered()
    {
        AddEvent(ReadingEventKind.Opened, Now.AddDays(-3).AddHours(-1));
        AddEvent(ReadingEventKind.Finished, Now.AddDays(-3));
        var prefs = new InsightsPreferences(_prefsPath);
        var vm = NewHistory(prefs);

        await vm.RefreshAsync();

        Assert.True(vm.ShowMilestones);
        Assert.Contains(vm.Milestones, m => m.Id == "finished-1" && m.IsEarned);
        var next = vm.Milestones.Single(m => m.Id == "finished-10");
        Assert.True(next.IsLocked);
        Assert.Equal("1 / 10", next.ProgressText);

        vm.ToggleMilestonesCommand.Execute(null);
        Assert.False(vm.ShowMilestones);
        Assert.True(vm.ShowMilestonesHeader); // the header stays so it can be turned back on
        Assert.Equal("Show", vm.MilestonesToggleText);
        Assert.True(new InsightsPreferences(_prefsPath).HideMilestones);

        var reopened = NewHistory(new InsightsPreferences(_prefsPath));
        await reopened.RefreshAsync();
        Assert.False(reopened.ShowMilestones);
        Assert.True(reopened.MilestonesHidden);
    }

    [Fact]
    public async Task BackfillOnlyHistory_ExplainsInsteadOfFakingMilestones()
    {
        var t = Now.AddDays(-60);
        AddEvent(ReadingEventKind.Opened, t);
        AddEvent(ReadingEventKind.Finished, t); // import signature: both at the same instant
        var vm = NewHistory();

        await vm.RefreshAsync();

        Assert.False(vm.ShowMilestones);
        Assert.True(vm.ShowBackfillNote);
        Assert.Empty(vm.Milestones);
    }

    [Fact]
    public async Task NoHistoryAtAll_ShowsNoStrip()
    {
        var vm = NewHistory();

        await vm.RefreshAsync();

        Assert.False(vm.ShowMilestonesHeader);
        Assert.False(vm.ShowBackfillNote);
    }

    // --- InsightsPreferences --------------------------------------------------------------------

    [Fact]
    public void Preferences_RoundTripThroughTheFile_AndSurviveGarbage()
    {
        var prefs = new InsightsPreferences(_prefsPath);
        Assert.False(prefs.HideMilestones);

        prefs.HideMilestones = true;
        Assert.True(new InsightsPreferences(_prefsPath).HideMilestones);

        File.WriteAllText(_prefsPath, "{ not json");
        Assert.False(new InsightsPreferences(_prefsPath).HideMilestones);

        var inMemory = new InsightsPreferences();
        inMemory.HideMilestones = true;
        Assert.True(inMemory.HideMilestones);
    }

    private sealed class NoDialogs : IDialogService
    {
        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(0);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(false);
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
