using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Smart features S2 on the App side (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.1, §4.2, §4.4): a session's active
/// time is stored with its page delta, and Home turns that into Up Next rows and time-left estimates.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SmartFeaturesReadingTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public SmartFeaturesReadingTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smart_reading_{Guid.NewGuid():N}.db");
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
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    // ----- ReaderActivityTracker -----

    [Fact]
    public void Tracker_CountsTimeWhileInputKeepsComing_AndStopsCountingOnceIdle()
    {
        var now = new DateTime(2026, 10, 6, 9, 0, 0);
        var tracker = new ReaderActivityTracker(() => now, useTimer: false);
        tracker.Start();

        for (int i = 0; i < 6; i++)     // one minute of page turns, ticked every 10 seconds
        {
            now = now.AddSeconds(10);
            tracker.NoteInput();
            tracker.Tick();
        }

        for (int i = 0; i < 60; i++)    // then ten minutes with the book open and nobody there
        {
            now = now.AddSeconds(10);
            tracker.Tick();
        }

        // The minute of reading, plus the two-minute idle allowance after the last page turn.
        Assert.Equal(180, tracker.Stop());
    }

    [Fact]
    public void Tracker_StartForgetsThePreviousItem_AndStopWithoutStartIsZero()
    {
        var now = new DateTime(2026, 10, 6, 9, 0, 0);
        var tracker = new ReaderActivityTracker(() => now, useTimer: false);
        Assert.Equal(0, tracker.Stop());

        tracker.Start();
        now = now.AddSeconds(40);
        tracker.NoteInput();
        Assert.Equal(40, tracker.Stop());

        tracker.Start();
        now = now.AddSeconds(15);
        tracker.NoteInput();
        Assert.Equal(15, tracker.Stop());
        Assert.Equal(0, tracker.Stop());
    }

    // ----- ReadingEventRecorder -----

    private static ReadingEvent OnlyEvent()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.ReadingEvents.Single();
    }

    [Fact]
    public void Recorder_StoresActiveSeconds_OnTheSameRowAsThePageDelta()
    {
        var recorder = new ReadingEventRecorder();
        recorder.RecordOpened(ReadingItemType.Comic, itemId: 7, seriesId: null, publisher: null, primaryGenre: null);

        recorder.UpdateSessionPages(ReadingItemType.Comic, 7, pagesRead: 22, activeSeconds: 660);

        var row = OnlyEvent();
        Assert.Equal(22, row.PagesRead);
        Assert.Equal(660, row.ActiveSeconds);
    }

    [Fact]
    public void Recorder_LeavesActiveSecondsNull_WhenThereIsNoneOrTheOldOverloadIsUsed()
    {
        var recorder = new ReadingEventRecorder();
        recorder.RecordOpened(ReadingItemType.Comic, 7, null, null, null);
        recorder.UpdateSessionPages(ReadingItemType.Comic, 7, pagesRead: 22, activeSeconds: 0);
        Assert.Null(OnlyEvent().ActiveSeconds);

        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ReadingEvents.ExecuteDelete();
        }

        recorder.RecordOpened(ReadingItemType.Comic, 7, null, null, null);
        recorder.UpdateSessionPages(ReadingItemType.Comic, 7, pagesRead: 22);
        Assert.Equal(22, OnlyEvent().PagesRead);
        Assert.Null(OnlyEvent().ActiveSeconds);
    }

    // ----- Home: Up Next and time-left -----

    private static (int SeriesId, List<int> IssueIds) SeedSeries(string name, int read, int unread, DateTime? lastOpened, int inProgress = 0)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name };
        int number = 1;
        for (int i = 0; i < read; i++)
        {
            series.Issues.Add(new Issue { Number = (number++).ToString(), PageCount = 20, LastPageRead = 19, OpenedTime = lastOpened, FilePath = $@"C:\x\{name}-{number}.cbz" });
        }

        for (int i = 0; i < inProgress; i++)
        {
            series.Issues.Add(new Issue { Number = (number++).ToString(), PageCount = 20, LastPageRead = 5, OpenedTime = lastOpened, FilePath = $@"C:\x\{name}-{number}.cbz" });
        }

        for (int i = 0; i < unread; i++)
        {
            series.Issues.Add(new Issue { Number = (number++).ToString(), PageCount = 20, FilePath = $@"C:\x\{name}-{number}.cbz" });
        }

        context.Series.Add(series);
        context.SaveChanges();
        return (series.Id, series.Issues.Select(i => i.Id).ToList());
    }

    /// <summary>Five timed sessions at one minute a page: enough history for a reading pace.</summary>
    private static void SeedPace()
    {
        using var context = PaperbunkrDb.CreateContext();
        for (int i = 0; i < 5; i++)
        {
            context.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Comic, ItemId = 9000 + i, Kind = ReadingEventKind.Opened,
                TimestampUtc = DateTime.UtcNow.AddDays(-i), PagesRead = 10, ActiveSeconds = 600,
            });
        }

        context.SaveChanges();
    }

    private static HomeScreenViewModel MakeHome(Action<int>? goReader = null) =>
        new(_ => { }, goReader ?? (_ => { }), _ => { }, (_, _) => { }, (_, _) => { });

    [Fact]
    public void Home_UpNext_ListsTheNextIssueOfStartedSeries_RightAfterContinueReading_AndOpensItInTheReader()
    {
        var (_, sagaIssues) = SeedSeries("Saga", read: 2, unread: 5, DateTime.UtcNow.AddDays(-3));
        SeedSeries("Reading now", read: 1, unread: 3, DateTime.UtcNow, inProgress: 1);
        int? opened = null;
        var vm = MakeHome(id => opened = id);

        var row = Assert.Single(vm.UpNext);
        Assert.Equal(1, row.Rank);
        Assert.Equal("Saga #3", row.Title);
        Assert.Equal(sagaIssues[2], row.IssueId);
        Assert.False(row.HasTimeLeft);     // no reading pace yet, so no estimate is invented

        var keys = vm.Sections.Select(s => s.Key).ToList();
        Assert.Equal(keys.IndexOf(HomeSectionKey.ContinueReading) + 1, keys.IndexOf(HomeSectionKey.UpNext));

        vm.OpenUpNextCommand.Execute(row);
        Assert.Equal(sagaIssues[2], opened);
    }

    [Fact]
    public void Home_UpNext_IsLeftOutOfTheSections_WhenThereIsNothingToSuggest()
    {
        SeedSeries("Never opened", read: 0, unread: 4, lastOpened: null);

        var vm = MakeHome();

        Assert.Empty(vm.UpNext);
        Assert.False(vm.HasUpNext);
        Assert.DoesNotContain(HomeSectionKey.UpNext, vm.Sections.Select(s => s.Key));
    }

    [Fact]
    public void Home_WithAReadingPace_ShowsTimeLeft_OnUpNextRowsAndContinueReadingCards()
    {
        SeedPace();
        SeedSeries("Saga", read: 2, unread: 5, DateTime.UtcNow.AddDays(-3));
        SeedSeries("Reading now", read: 0, unread: 0, DateTime.UtcNow, inProgress: 1);

        var vm = MakeHome();

        Assert.Equal("~20 min", Assert.Single(vm.UpNext).TimeLeft);                       // 20 unread pages at a minute a page
        Assert.Equal("~15 min left", Assert.Single(vm.ContinueReading).Meta);             // pages 6-20 still to go
    }

    [Fact]
    public void Home_HidingUpNextInPreferences_SkipsItEntirely()
    {
        SeedSeries("Saga", read: 2, unread: 5, DateTime.UtcNow.AddDays(-3));
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.GetOrCreateAppSettings().HomeHiddenSections = HomeSectionKey.UpNext;
            context.SaveChanges();
        }

        var vm = MakeHome();

        Assert.Empty(vm.UpNext);
        Assert.DoesNotContain(HomeSectionKey.UpNext, vm.Sections.Select(s => s.Key));
    }
}
