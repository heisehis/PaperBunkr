using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests.ReadingLists;

/// <summary>
/// The pure/data halves of docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md: the completion forecast (§5),
/// overlap detection and merge (§7), and the CSV/text/checklist exports (§6).
/// </summary>
public class ReadingListTrackEnginesTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_track_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public ReadingListTrackEnginesTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext() => new(_options);

    // --- Forecast ---

    [Fact]
    public void Forecast_NeedsFiveFinishedInTheWindow()
    {
        var four = Enumerable.Range(1, 4).Select(d => Now.AddDays(-d)).ToList();
        Assert.Null(ReadingListForecast.Compute(four, 10, 0, Now));

        var fiveButOneTooOld = four.Append(Now.AddDays(-91)).ToList();
        Assert.Null(ReadingListForecast.Compute(fiveButOneTooOld, 10, 0, Now));

        Assert.NotNull(ReadingListForecast.Compute(four.Append(Now.AddDays(-89)).ToList(), 10, 0, Now));
    }

    [Fact]
    public void Forecast_ProjectsFromTheNinetyDayPace_AndCarriesMissing()
    {
        // 9 finished in 90 days = 0.1/day; 12 unread -> 120 days.
        var finished = Enumerable.Range(1, 9).Select(d => Now.AddDays(-d * 5)).ToList();
        var result = ReadingListForecast.Compute(finished, 12, 2, Now)!;
        Assert.Equal(Now.AddDays(120), result.FinishByUtc);
        Assert.Equal(2, result.MissingUnread);
        Assert.Null(ReadingListForecast.Compute(finished, 0, 0, Now));
    }

    [Theory]
    [InlineData(0.5, "~tomorrow")]
    [InlineData(5, "~in 5 days")]
    [InlineData(21, "~in 3 weeks")]
    public void Forecast_FormatsNearDatesRelatively(double days, string expected) =>
        Assert.Equal(expected, ReadingListForecast.FormatFinishBy(Now.AddDays(days), Now));

    [Fact]
    public void Forecast_LoadsOnlyFinishedComicsInTheWindow()
    {
        using (var context = NewContext())
        {
            context.ReadingEvents.AddRange(
                new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 1, Kind = ReadingEventKind.Finished, TimestampUtc = Now.AddDays(-1) },
                new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 1, Kind = ReadingEventKind.Finished, TimestampUtc = Now.AddDays(-2) }, // re-read counts
                new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 2, Kind = ReadingEventKind.Opened, TimestampUtc = Now.AddDays(-1) },
                new ReadingEvent { ItemType = ReadingItemType.Novel, ItemId = 3, Kind = ReadingEventKind.Finished, TimestampUtc = Now.AddDays(-1) },
                new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 4, Kind = ReadingEventKind.Finished, TimestampUtc = Now.AddDays(-100) });
            context.SaveChanges();
        }

        using var check = NewContext();
        Assert.Equal(2, ReadingListForecast.LoadFinishedComicTimestamps(check, Now).Count);
    }

    // --- Overlap ---

    private static (int, IReadOnlySet<int>) L(int id, params int[] issues) => (id, issues.ToHashSet());

    [Fact]
    public void Overlap_FlagsSixtyPercentOfTheSmallerList()
    {
        var lists = new[] { L(1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10), L(2, 1, 2, 3, 20, 21), L(3, 1, 2, 3, 4, 30) };
        var pairs = ReadingListOverlap.Find(lists, new HashSet<(int, int)>());

        // (1,2): 3 of 5 = 60% -> in. (1,3): 4 of 5 = 80% -> in, and first. (2,3): 3 of 5 -> in.
        Assert.Equal(new[] { (1, 3), (1, 2), (2, 3) }, pairs.Select(p => (p.ListA, p.ListB)));
        Assert.Equal(4, pairs[0].Shared);
        Assert.Equal(5, pairs[0].SmallerCount);
    }

    [Fact]
    public void Overlap_IgnoresTinyLists_BelowThreshold_AndDismissedPairs()
    {
        var lists = new[] { L(1, 1, 2, 3, 4), L(2, 1, 2, 3, 4), L(3, 10, 11, 12, 13, 14), L(4, 10, 11, 20, 21, 22), L(5, 10, 11, 12, 13, 99) };
        var pairs = ReadingListOverlap.Find(lists, new HashSet<(int, int)> { (3, 5) });

        Assert.Empty(pairs);   // 1&2 too small; 3&4 only 40%; 3&5 dismissed; 4&5 40%
    }

    // --- Merge ---

    private (int Into, int From, int[] Issues) SeedMergeLists()
    {
        using var context = NewContext();
        var series = new Series { Name = "Crisis" };
        var issues = Enumerable.Range(1, 6).Select(n => new Issue { Series = series, Number = n.ToString() }).ToList();
        context.Issues.AddRange(issues);
        context.SaveChanges();
        int[] ids = issues.Select(i => i.Id).ToArray();

        // into: 1, 3, 5      from: 0?, 2 (before anything shared) ... use: from = 2, 1, 4, 3, 6 with notes
        var into = new ReadingList { Name = "Into" };
        into.Items.Add(new ReadingListItem { IssueId = ids[0], SortOrder = 0 });
        into.Items.Add(new ReadingListItem { IssueId = ids[2], SortOrder = 1, Notes = "keep mine" });
        into.Items.Add(new ReadingListItem { IssueId = ids[4], SortOrder = 2 });
        var from = new ReadingList { Name = "From" };
        from.Items.Add(new ReadingListItem { IssueId = ids[1], SortOrder = 0 });
        from.Items.Add(new ReadingListItem { IssueId = ids[0], SortOrder = 1, Notes = "filled", Role = EventMembershipRole.Core });
        from.Items.Add(new ReadingListItem { IssueId = ids[3], SortOrder = 2 });
        from.Items.Add(new ReadingListItem { IssueId = ids[2], SortOrder = 3, Notes = "theirs" });
        from.Items.Add(new ReadingListItem { IssueId = ids[5], SortOrder = 4 });
        from.Tags.Add(new ReadingListTag { Value = "crisis" });
        context.ReadingLists.AddRange(into, from);
        context.SaveChanges();
        return (into.Id, from.Id, ids);
    }

    [Fact]
    public void Merge_InsertsAfterTheLastSharedIssue_FillsBlanks_AndDeletesTheOther()
    {
        var (intoId, fromId, ids) = SeedMergeLists();
        var events = new LibraryEvents();
        var heard = new List<ReadingListChangedEvent>();
        events.ReadingListChanged += heard.Add;

        using (var context = NewContext())
        {
            Assert.Equal(3, ReadingListMerger.CountToAdd(context, intoId, fromId));
            var result = ReadingListMerger.Merge(context, intoId, fromId, deleteOther: true, events);
            context.SaveChanges();
            Assert.Equal(3, result.AddedCount);
            Assert.Equal(1, result.FilledCount);
        }

        using var check = NewContext();
        var items = check.ReadingListItems.Where(i => i.ReadingListId == intoId).OrderBy(i => i.SortOrder).ToList();
        // 2 had nothing shared before it -> top; 4 follows shared 1; 6 follows shared 3.
        Assert.Equal(new[] { ids[1], ids[0], ids[3], ids[2], ids[5], ids[4] }, items.Select(i => i.IssueId));
        Assert.Equal("filled", items[1].Notes);
        Assert.Equal(EventMembershipRole.Core, items[1].Role);
        Assert.Equal("keep mine", items[3].Notes);
        Assert.Null(check.ReadingLists.Find(fromId));
        Assert.Contains(check.ReadingListTags.Where(t => t.ReadingListId == intoId), t => t.Value == "crisis");
        var announced = Assert.Single(heard);
        Assert.Equal(3, announced.AddedIssueIds.Count);
    }

    [Fact]
    public void Merge_CanKeepTheOtherList()
    {
        var (intoId, fromId, _) = SeedMergeLists();
        using (var context = NewContext())
        {
            ReadingListMerger.Merge(context, intoId, fromId, deleteOther: false);
            context.SaveChanges();
        }

        using var check = NewContext();
        Assert.NotNull(check.ReadingLists.Find(fromId));
        Assert.Equal(5, check.ReadingListItems.Count(i => i.ReadingListId == fromId));
    }

    // --- Exports ---

    private int SeedExportList()
    {
        using var context = NewContext();
        var series = new Series { Name = "Crisis, Infinite" };
        var owned = new Issue { Series = series, Number = "1", Year = 1985, FilePath = "c:/x.cbz", PageCount = 10, LastPageRead = 9 };
        var missing = new Issue { Series = series, Number = "2", Year = 1985, IsPlaceholder = true, FileIsMissing = true };
        var list = new ReadingList { Name = "Crisis" };
        list.Items.Add(new ReadingListItem { Issue = owned, SortOrder = 0, GroupLabel = "Prelude" });
        list.Items.Add(new ReadingListItem { Issue = missing, SortOrder = 1, Notes = "read after #1,\nthen \"stop\"" });
        context.ReadingLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }

    [Fact]
    public void CsvWrite_QuotesAndFlattensNotes_AndReImports()
    {
        int listId = SeedExportList();
        string csv;
        using (var context = NewContext())
        {
            csv = CsvReadingListIO.Write(context, listId);
        }

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Series,Number,Volume,Year,Format,Read,Owned,Note", lines[0]);
        Assert.Equal("\"Crisis, Infinite\",1,,1985,,yes,yes,", lines[1]);
        Assert.Equal("\"Crisis, Infinite\",2,,1985,,no,no,\"read after #1, then \"\"stop\"\"\"", lines[2]);

        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_csv_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, csv);
            using var context = NewContext();
            var imported = CsvReadingListIO.Import(context, path);
            var originalIssueIds = context.ReadingListItems.Where(i => i.ReadingListId == listId).OrderBy(i => i.SortOrder).Select(i => i.IssueId).ToList();
            Assert.Equal(originalIssueIds, imported.List.Items.OrderBy(i => i.SortOrder).Select(i => i.IssueId));
            Assert.Empty(imported.SkippedRows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TextExport_AppendsNotesOnTheSameLine()
    {
        int listId = SeedExportList();
        using var context = NewContext();
        string text = ReadingListTextExporter.Export(context, listId);
        Assert.Contains("1. Crisis, Infinite #1 (1985)" + Environment.NewLine, text);
        Assert.Contains("2. Crisis, Infinite #2 (1985) — read after #1, then \"stop\"", text);
    }

    [Fact]
    public void Checklist_CarriesGroups_Status_AndNotes()
    {
        int listId = SeedExportList();
        using var context = NewContext();
        var model = ReadingListChecklist.Build(context, listId, new DateTime(2026, 9, 28));

        Assert.Equal("Crisis", model.Title);
        Assert.Equal((2, 1, 1), (model.IssueCount, model.ReadCount, model.MissingCount));
        Assert.Equal("Prelude", model.Rows[0].GroupLabel);
        Assert.True(model.Rows[0].IsRead);
        Assert.True(model.Rows[0].IsOwned);
        Assert.False(model.Rows[1].IsOwned);
        Assert.Equal("Crisis, Infinite #2", model.Rows[1].Display);
        Assert.StartsWith("read after", model.Rows[1].Note);
        Assert.Contains("printed 28 Sep 2026 from Paperbunkr", model.MetaLine);
    }
}
