using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The producers behind the Plugin API 4.1 domain hooks (docs/superpowers/specs/2026-09-20-plugin-api-4-1-
/// design.md §5): the folder scanner, Library Health, the reading-event recorder and the deletion
/// helper each raise the right event at the right moment - and stay quiet otherwise. Every test hands
/// its own <see cref="LibraryEvents"/> to the producer, so tests can't hear each other. Real files and a
/// real SQLite database, same precedent as <see cref="LibraryFolderScannerTests"/>.
/// </summary>
public class DomainEventProducerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _root;
    private readonly LibraryEvents _events = new();

    public DomainEventProducerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_domainevents_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_domainevents_root_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext() => new(_dbOptions);

    private void AddWatchedFolder(string path)
    {
        using var context = NewContext();
        context.WatchedFolders.Add(new WatchedFolder { Path = path });
        context.SaveChanges();
    }

    // ---------------------------------------------------------------- LibraryScanCompleted

    private LibraryFolderScanner CreateScanner() => new(NewContext, _events);

    [Fact]
    public async Task A_full_scan_raises_LibraryScanCompleted_with_what_it_added()
    {
        CbzFixture.Create(Path.Combine(_root, "Kilo Station 001 (2021).cbz"), pageCount: 1);
        CbzFixture.Create(Path.Combine(_root, "Kilo Station 002 (2021).cbz"), pageCount: 1);
        AddWatchedFolder(_root);
        var heard = new List<LibraryScanCompletedEvent>();
        _events.LibraryScanCompleted += heard.Add;

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        var scan = Assert.Single(heard);
        Assert.Equal(new[] { _root }, scan.FolderPaths);
        Assert.Equal(2, scan.AddedCount);
        Assert.Equal(result.SeriesTouched, scan.SeriesTouched);
        Assert.Equal(result.AddedIssueIds, scan.AddedItemIds);
        Assert.True(scan.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task UpdatedCount_is_zero_and_UpdatedItemIds_is_an_empty_reserved_list()
    {
        CbzFixture.Create(Path.Combine(_root, "Kilo Station 001 (2021).cbz"), pageCount: 1);
        AddWatchedFolder(_root);
        var heard = new List<LibraryScanCompletedEvent>();
        _events.LibraryScanCompleted += heard.Add;

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        var scan = Assert.Single(heard);
        Assert.Equal(0, scan.UpdatedCount);
        Assert.Empty(scan.UpdatedItemIds);
    }

    [Fact]
    public async Task A_scan_that_finds_nothing_still_completes_once()
    {
        CbzFixture.Create(Path.Combine(_root, "Kilo Station 001 (2021).cbz"), pageCount: 1);
        AddWatchedFolder(_root);
        var scanner = CreateScanner();
        await scanner.ScanAllAsync(new Progress<(int, int)>());
        var heard = new List<LibraryScanCompletedEvent>();
        _events.LibraryScanCompleted += heard.Add;

        await scanner.ScanAllAsync(new Progress<(int, int)>());

        var scan = Assert.Single(heard);
        Assert.Equal(0, scan.AddedCount);
        Assert.Empty(scan.AddedItemIds);
    }

    [Fact]
    public async Task Only_folders_that_exist_are_listed_as_scanned()
    {
        AddWatchedFolder(_root);
        AddWatchedFolder(Path.Combine(_root, "not-there"));
        var heard = new List<LibraryScanCompletedEvent>();
        _events.LibraryScanCompleted += heard.Add;

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(new[] { _root }, Assert.Single(heard).FolderPaths);
    }

    [Fact]
    public async Task A_live_watch_or_drag_import_is_not_a_scan_and_raises_nothing()
    {
        string file = Path.Combine(_root, "Kilo Station 001 (2021).cbz");
        CbzFixture.Create(file, pageCount: 1);
        var heard = new List<LibraryScanCompletedEvent>();
        _events.LibraryScanCompleted += heard.Add;

        var result = await CreateScanner().ImportNewFilesAsync(new[] { file }, new Progress<(int, int)>());

        Assert.Equal(1, result.IssuesAdded);
        Assert.Empty(heard);
    }

    [Fact]
    public async Task A_cancelled_scan_raises_nothing()
    {
        CbzFixture.Create(Path.Combine(_root, "Kilo Station 001 (2021).cbz"), pageCount: 1);
        AddWatchedFolder(_root);
        var heard = new List<LibraryScanCompletedEvent>();
        _events.LibraryScanCompleted += heard.Add;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateScanner().ScanAllAsync(new Progress<(int, int)>(), cancelled.Token));

        Assert.Empty(heard);
    }

    // ---------------------------------------------------------------- MissingFileDetected

    private LibraryHealthService CreateHealth() => new(NewContext, _events);

    private int AddIssue(string fileName, bool createFile, bool acknowledged = false, string? title = null)
    {
        string path = Path.Combine(_root, fileName);
        if (createFile)
        {
            File.WriteAllText(path, "fixture");
        }

        using var context = NewContext();
        var series = new Series { Name = "Test Series" };
        var issue = new Issue { Series = series, Number = "1", FilePath = path, MissingAcknowledged = acknowledged, Title = title };
        context.Series.Add(series);
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static Progress<(int, int)> NoProgress() => new();

    [Fact]
    public async Task A_missing_file_is_announced_on_the_pass_it_becomes_confirmed_and_not_before()
    {
        int issueId = AddIssue("gone.cbz", createFile: false);
        var heard = new List<MissingFileConfirmedEvent>();
        _events.MissingFileConfirmed += heard.Add;
        var health = CreateHealth();

        await health.VerifyAsync(NoProgress());
        Assert.Empty(heard);   // one failed check is not enough - a disconnected drive would be noisy

        await health.VerifyAsync(NoProgress());
        var confirmed = Assert.Single(heard);
        Assert.Equal(ReadingItemType.Comic, confirmed.ItemType);
        Assert.Equal(issueId, confirmed.ItemId);
        Assert.EndsWith("gone.cbz", confirmed.FilePath);
        Assert.Equal("gone", confirmed.Title);   // no Title set - falls back to the file name
    }

    [Fact]
    public async Task A_file_that_stays_missing_is_not_announced_again_on_later_passes()
    {
        AddIssue("gone.cbz", createFile: false);
        var heard = new List<MissingFileConfirmedEvent>();
        _events.MissingFileConfirmed += heard.Add;
        var health = CreateHealth();

        await health.VerifyAsync(NoProgress());
        await health.VerifyAsync(NoProgress());
        await health.VerifyAsync(NoProgress());
        await health.VerifyAsync(NoProgress());

        Assert.Single(heard);
    }

    [Fact]
    public async Task A_file_that_reappears_and_goes_missing_again_is_announced_again()
    {
        int issueId = AddIssue("flaky.cbz", createFile: false);
        string path = Path.Combine(_root, "flaky.cbz");
        var heard = new List<MissingFileConfirmedEvent>();
        _events.MissingFileConfirmed += heard.Add;
        var health = CreateHealth();

        await health.VerifyAsync(NoProgress());
        await health.VerifyAsync(NoProgress());
        Assert.Single(heard);

        File.WriteAllText(path, "back");
        await health.VerifyAsync(NoProgress());     // reappears: count resets
        File.Delete(path);
        await health.VerifyAsync(NoProgress());
        await health.VerifyAsync(NoProgress());     // crosses the threshold a second time

        Assert.Equal(2, heard.Count);
        Assert.All(heard, h => Assert.Equal(issueId, h.ItemId));
    }

    [Fact]
    public async Task An_acknowledged_missing_file_is_never_announced()
    {
        AddIssue("gone.cbz", createFile: false, acknowledged: true);
        var heard = new List<MissingFileConfirmedEvent>();
        _events.MissingFileConfirmed += heard.Add;
        var health = CreateHealth();

        await health.VerifyAsync(NoProgress());
        await health.VerifyAsync(NoProgress());
        await health.VerifyAsync(NoProgress());

        Assert.Empty(heard);
    }

    [Fact]
    public async Task The_announcement_follows_the_configured_threshold()
    {
        AddIssue("gone.cbz", createFile: false);
        var heard = new List<MissingFileConfirmedEvent>();
        _events.MissingFileConfirmed += heard.Add;
        var health = CreateHealth();
        health.ConfirmedMissingThreshold = 3;

        await health.VerifyAsync(NoProgress());
        await health.VerifyAsync(NoProgress());
        Assert.Empty(heard);

        await health.VerifyAsync(NoProgress());
        Assert.Single(heard);
    }

    [Fact]
    public async Task Existing_files_and_a_title_are_reported_correctly()
    {
        AddIssue("here.cbz", createFile: true);
        AddIssue("gone.cbz", createFile: false, title: "The Real Title");
        var heard = new List<MissingFileConfirmedEvent>();
        _events.MissingFileConfirmed += heard.Add;
        var health = CreateHealth();

        await health.VerifyAsync(NoProgress());
        await health.VerifyAsync(NoProgress());

        var confirmed = Assert.Single(heard);
        Assert.Equal("The Real Title", confirmed.Title);
    }

    // ---------------------------------------------------------------- BookRead (recorder)

    [Fact]
    public void RecordFinished_raises_ReadingFinished_with_the_saved_row()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        var heard = new List<ReadingEvent>();
        recorder.ReadingFinished += heard.Add;

        recorder.RecordFinished(ReadingItemType.Comic, itemId: 42, seriesId: 7, publisher: "Marvel", primaryGenre: "Superhero", pagesRead: 22);

        var finished = Assert.Single(heard);
        Assert.Equal(ReadingEventKind.Finished, finished.Kind);
        Assert.Equal(ReadingItemType.Comic, finished.ItemType);
        Assert.Equal(42, finished.ItemId);
        Assert.Equal(7, finished.SeriesId);
        Assert.Equal(22, finished.PagesRead);
        Assert.NotEqual(0, finished.Id);   // raised after the save, so the row has its Id

        using var context = NewContext();
        Assert.Equal(finished.Id, Assert.Single(context.ReadingEvents).Id);
    }

    [Fact]
    public void Opening_an_item_does_not_raise_ReadingFinished()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        var heard = new List<ReadingEvent>();
        recorder.ReadingFinished += heard.Add;

        recorder.RecordOpened(ReadingItemType.Novel, itemId: 1, seriesId: null, publisher: null, primaryGenre: null);

        Assert.Empty(heard);
    }

    [Fact]
    public void A_re_read_raises_ReadingFinished_again()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        var heard = new List<ReadingEvent>();
        recorder.ReadingFinished += heard.Add;

        recorder.RecordFinished(ReadingItemType.Comic, 1, null, null, null, pagesRead: 20);
        recorder.RecordFinished(ReadingItemType.Comic, 1, null, null, null, pagesRead: 20);

        Assert.Equal(2, heard.Count);
        Assert.NotEqual(heard[0].Id, heard[1].Id);
    }

    [Fact]
    public void A_throwing_ReadingFinished_handler_does_not_break_recording()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        recorder.ReadingFinished += _ => throw new InvalidOperationException("bad handler");

        recorder.RecordFinished(ReadingItemType.Comic, 1, null, null, null, pagesRead: 5);   // must not throw

        using var context = NewContext();
        Assert.Single(context.ReadingEvents);
    }

    [Fact]
    public void The_finished_count_still_reaches_the_existing_ReadingEventRecorded_subscribers()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        int recorded = 0;
        recorder.ReadingEventRecorded += () => recorded++;

        recorder.RecordFinished(ReadingItemType.Comic, 1, null, null, null, pagesRead: 5);

        Assert.Equal(1, recorded);
    }

    // ---------------------------------------------------------------- ReadingListChanged (deletion)

    [Fact]
    public void Deleting_an_issue_announces_its_removal_from_each_list_it_was_in()
    {
        // LibraryDeletionHelper has no events parameter (it is a static helper called from many places),
        // so this listens on the default hub and filters to this test's own lists.
        int issueId, listA, listB;
        using (var setup = NewContext())
        {
            var series = new Series { Name = "S" };
            var issue = new Issue { Series = series, Number = "1", FileIsMissing = true };
            var a = new ReadingList { Name = "List A", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var b = new ReadingList { Name = "List B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            setup.AddRange(series, issue, a, b);
            setup.SaveChanges();
            setup.ReadingListItems.AddRange(
                new ReadingListItem { ReadingListId = a.Id, IssueId = issue.Id, SortOrder = 0 },
                new ReadingListItem { ReadingListId = b.Id, IssueId = issue.Id, SortOrder = 0 });
            setup.SaveChanges();
            (issueId, listA, listB) = (issue.Id, a.Id, b.Id);
        }

        var heard = new List<ReadingListChangedEvent>();
        void Handler(ReadingListChangedEvent e)
        {
            lock (heard)
            {
                if (e.ListId == listA || e.ListId == listB)
                {
                    heard.Add(e);
                }
            }
        }

        LibraryEvents.Default.ReadingListChanged += Handler;
        try
        {
            using var context = NewContext();
            var issue = context.Issues.Find(issueId)!;
            LibraryDeletionHelper.RemoveIssue(context, issue);
            context.SaveChanges();
        }
        finally
        {
            LibraryEvents.Default.ReadingListChanged -= Handler;
        }

        lock (heard)
        {
            Assert.Equal(2, heard.Count);
            Assert.All(heard, e =>
            {
                Assert.Equal(ReadingListChangeKind.Removed, e.Kind);
                Assert.Equal(new[] { issueId }, e.RemovedIssueIds);
            });
        }

        using var verify = NewContext();
        Assert.Empty(verify.ReadingListItems);
    }
}
