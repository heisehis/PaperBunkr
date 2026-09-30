using System.Diagnostics;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.History;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReadingHistoryResolver"/> (docs/superpowers/specs/2026-09-29-insights-reading-history-
/// design.md §2 and §6) against a temp SQLite database, writing events through the real
/// <see cref="ReadingEventRecorder"/> where snapshot names matter.
/// </summary>
public class ReadingHistoryResolverTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath;

    public ReadingHistoryResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_history_resolver_{Guid.NewGuid():N}.db");
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
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

    private PaperbunkrDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

    private IReadOnlyList<ReadingHistoryRow> Resolve()
    {
        using var ctx = NewContext();
        return ReadingHistoryResolver.Resolve(ctx);
    }

    // ----- seeding helpers -----

    private (Series Series, List<Issue> Issues) SeedSeries(string name, ContentType type = ContentType.Comic, int issueCount = 3, int? remoteSourceId = null)
    {
        using var ctx = NewContext();
        var series = new Series { Name = name, ContentType = type, RemoteSourceId = remoteSourceId };
        ctx.Series.Add(series);
        ctx.SaveChanges();
        var issues = Enumerable.Range(1, issueCount)
            .Select(n => new Issue { SeriesId = series.Id, Number = n.ToString(), PageCount = 20, FilePath = $"{name}-{n}.cbz", RemoteSourceId = remoteSourceId })
            .ToList();
        ctx.Issues.AddRange(issues);
        ctx.SaveChanges();
        return (series, issues);
    }

    private Book SeedBook(string title, BookFormat format = BookFormat.Epub, int? bookSeriesId = null, bool finished = false, double? progress = null)
    {
        using var ctx = NewContext();
        var book = new Book { Title = title, FilePath = $"{title}.epub", Format = format, BookSeriesId = bookSeriesId, Finished = finished, LastProgressionFraction = progress };
        ctx.Books.Add(book);
        ctx.SaveChanges();
        return book;
    }

    private void SetLastPage(int issueId, int? lastPageRead)
    {
        using var ctx = NewContext();
        ctx.Issues.Single(i => i.Id == issueId).LastPageRead = lastPageRead;
        ctx.SaveChanges();
    }

    /// <summary>Records through the real recorder (so snapshots are filled), then pins the timestamp for ordering.</summary>
    private void Read(ReadingItemType type, int itemId, int? seriesId, DateTime at, ReadingEventKind kind = ReadingEventKind.Opened)
    {
        var recorder = new ReadingEventRecorder(NewContext);
        if (kind == ReadingEventKind.Opened)
        {
            recorder.RecordOpened(type, itemId, seriesId, null, null);
        }
        else
        {
            recorder.RecordFinished(type, itemId, seriesId, null, null, null);
        }

        using var ctx = NewContext();
        var row = ctx.ReadingEvents.OrderByDescending(e => e.Id).First();
        row.TimestampUtc = at;
        ctx.SaveChanges();
    }

    private void Delete<T>(int id) where T : class
    {
        using var ctx = NewContext();
        ctx.IncludeRemote = true;
        var entity = ctx.Find<T>(id)!;
        ctx.Remove(entity);
        ctx.SaveChanges();
    }

    // ----- grouping -----

    [Fact]
    public void OneRowPerSeries_NewestReadFirst_AndRereadMovesItUp()
    {
        var (saga, sagaIssues) = SeedSeries("Saga");
        var (op, opIssues) = SeedSeries("One Piece");

        Read(ReadingItemType.Comic, sagaIssues[0].Id, saga.Id, T0);
        Read(ReadingItemType.Comic, sagaIssues[1].Id, saga.Id, T0.AddHours(1));
        Read(ReadingItemType.Comic, opIssues[0].Id, op.Id, T0.AddHours(2));

        var rows = Resolve();
        Assert.Equal(new[] { "One Piece", "Saga" }, rows.Select(r => r.Title));
        Assert.Equal("#2", rows[1].ItemLabel);

        Read(ReadingItemType.Comic, sagaIssues[2].Id, saga.Id, T0.AddHours(3));
        rows = Resolve();
        Assert.Equal(new[] { "Saga", "One Piece" }, rows.Select(r => r.Title));
        Assert.Equal("#3", rows[0].ItemLabel);
        Assert.Equal(T0.AddHours(3), rows[0].LastReadUtc);
    }

    [Fact]
    public void ComicSeriesAndBookSeries_WithTheSameId_StayApartAsTwoRows()
    {
        var (saga, issues) = SeedSeries("Saga");
        BookSeries bookSeries;
        using (var ctx = NewContext())
        {
            // Force a book series whose id equals the comic series id.
            bookSeries = new BookSeries { Id = saga.Id, Name = "Kingkiller Chronicle" };
            ctx.BookSeries.Add(bookSeries);
            ctx.SaveChanges();
        }

        var book = SeedBook("The Name of the Wind", bookSeriesId: bookSeries.Id);
        Read(ReadingItemType.Comic, issues[0].Id, saga.Id, T0);
        Read(ReadingItemType.Novel, book.Id, bookSeries.Id, T0.AddHours(1));

        var rows = Resolve();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.GroupKey == new ReadingHistoryGroupKey(ReadingHistoryGroupKind.ComicSeries, saga.Id) && r.Title == "Saga");
        Assert.Contains(rows, r => r.GroupKey == new ReadingHistoryGroupKey(ReadingHistoryGroupKind.BookSeries, saga.Id) && r.Title == "Kingkiller Chronicle");
    }

    [Fact]
    public void BooksInASeries_CollapseToOneRow_StandaloneBooksGetTheirOwn()
    {
        int bookSeriesId;
        using (var ctx = NewContext())
        {
            var bs = new BookSeries { Name = "Kingkiller Chronicle" };
            ctx.BookSeries.Add(bs);
            ctx.SaveChanges();
            bookSeriesId = bs.Id;
        }

        var one = SeedBook("The Name of the Wind", bookSeriesId: bookSeriesId, finished: true);
        var two = SeedBook("The Wise Man's Fear", bookSeriesId: bookSeriesId, progress: 0.38);
        var solo = SeedBook("Piranesi");
        Read(ReadingItemType.Novel, one.Id, bookSeriesId, T0);
        Read(ReadingItemType.Novel, two.Id, bookSeriesId, T0.AddHours(1));
        Read(ReadingItemType.Novel, solo.Id, null, T0.AddHours(2));

        var rows = Resolve();
        Assert.Equal(2, rows.Count);
        var series = rows.Single(r => r.GroupKey.Kind == ReadingHistoryGroupKind.BookSeries);
        Assert.Equal(("Kingkiller Chronicle", "The Wise Man's Fear", "38%"), (series.Title, series.ItemLabel, series.ProgressText));
        Assert.Equal(ReadingHistoryAction.Resume, series.Action);
        Assert.Equal(two.Id, series.TargetId);
        var standalone = rows.Single(r => r.GroupKey.Kind == ReadingHistoryGroupKind.Book);
        Assert.Equal(("Piranesi", (string?)null), (standalone.Title, standalone.ItemLabel));
        Assert.Equal(solo.Id, standalone.DetailBookId);
    }

    // ----- hiding -----

    [Fact]
    public void HiddenGroups_AreExcluded_UntilReadAgain_ThenOnlyTheNewReadCounts()
    {
        var (saga, issues) = SeedSeries("Saga");
        Read(ReadingItemType.Comic, issues[2].Id, saga.Id, T0);
        var recorder = new ReadingEventRecorder(NewContext);

        recorder.HideFromHistory(new ReadingHistoryGroupKey(ReadingHistoryGroupKind.ComicSeries, saga.Id));
        Assert.Empty(Resolve());

        Read(ReadingItemType.Comic, issues[0].Id, saga.Id, T0.AddDays(1));
        var row = Assert.Single(Resolve());
        Assert.Equal("#1", row.ItemLabel);
        Assert.Equal(T0.AddDays(1), row.LastReadUtc);
    }

    [Fact]
    public void ClearAll_HidesEveryRow()
    {
        var (saga, issues) = SeedSeries("Saga");
        var book = SeedBook("Piranesi");
        Read(ReadingItemType.Comic, issues[0].Id, saga.Id, T0);
        Read(ReadingItemType.Novel, book.Id, null, T0);

        new ReadingEventRecorder(NewContext).HideFromHistory(null);

        Assert.Empty(Resolve());
    }

    // ----- deleted items -----

    [Fact]
    public void DeletedLastReadIssue_FallsBackToTheNewestSurvivingIssue()
    {
        var (saga, issues) = SeedSeries("Saga");
        Read(ReadingItemType.Comic, issues[0].Id, saga.Id, T0);
        Read(ReadingItemType.Comic, issues[1].Id, saga.Id, T0.AddHours(1));
        Delete<Issue>(issues[1].Id);

        var row = Assert.Single(Resolve());
        Assert.True(row.IsInLibrary);
        Assert.Equal("#1", row.ItemLabel);
        Assert.Equal(issues[0].Id, row.TargetId);
        Assert.Equal(T0.AddHours(1), row.LastReadUtc); // the group's newest read, not the fallback issue's
    }

    [Fact]
    public void DeletedSeries_IsGreyed_AndNamedFromTheSnapshot()
    {
        var (saga, issues) = SeedSeries("Saga");
        Read(ReadingItemType.Comic, issues[1].Id, saga.Id, T0, ReadingEventKind.Finished);
        using (var ctx = NewContext())
        {
            ctx.Issues.RemoveRange(ctx.Issues.Where(i => i.SeriesId == saga.Id));
            ctx.Series.Remove(ctx.Series.Single(s => s.Id == saga.Id));
            ctx.SaveChanges();
        }

        var row = Assert.Single(Resolve());
        Assert.False(row.IsInLibrary);
        Assert.Equal(("Saga", "#2"), (row.Title, row.ItemLabel));
        Assert.Equal(ReadingHistoryAction.None, row.Action);
        Assert.Null(row.CoverKey);
        Assert.Null(row.DetailSeriesId);
        Assert.True(row.IsFinished);
    }

    [Fact]
    public void DeletedStandaloneBook_IsGreyed()
    {
        var book = SeedBook("Piranesi");
        Read(ReadingItemType.Novel, book.Id, null, T0);
        Delete<Book>(book.Id);

        var row = Assert.Single(Resolve());
        Assert.False(row.IsInLibrary);
        Assert.Equal("Piranesi", row.Title);
        Assert.Equal(ReadingHistoryContentKind.Book, row.ContentKind);
    }

    [Fact]
    public void GoneItemWithNoSnapshot_IsDropped()
    {
        using (var ctx = NewContext())
        {
            // A pre-feature row whose item was deleted before the backfill ran: no names anywhere.
            ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 404, SeriesId = 77, Kind = ReadingEventKind.Opened, TimestampUtc = T0 });
            ctx.SaveChanges();
        }

        Assert.Empty(Resolve());
    }

    [Fact]
    public void RemoteLibraryItems_Resolve_AndGreyOutWhenTheSourceIsRemoved()
    {
        int sourceId;
        using (var ctx = NewContext())
        {
            var source = new RemoteSource { InstanceId = "peer", DisplayName = "Peer", Host = "10.0.0.2" };
            ctx.Add(source);
            ctx.SaveChanges();
            sourceId = source.Id;
        }

        var (remote, issues) = SeedSeries("Remote Saga", remoteSourceId: sourceId);
        Read(ReadingItemType.Comic, issues[0].Id, remote.Id, T0);

        var live = Assert.Single(Resolve());
        Assert.True(live.IsInLibrary);
        Assert.Equal("Remote Saga", live.Title);

        using (var ctx = NewContext())
        {
            // What RemoteLibraryService does on source removal.
            ctx.IncludeRemote = true;
            ctx.Issues.Where(i => i.RemoteSourceId == sourceId).ExecuteDelete();
            ctx.Series.Where(s => s.RemoteSourceId == sourceId).ExecuteDelete();
        }

        var greyed = Assert.Single(Resolve());
        Assert.False(greyed.IsInLibrary);
        Assert.Equal("Remote Saga", greyed.Title);
    }

    // ----- the ▶ table -----

    [Fact]
    public void UnfinishedIssue_Resumes_WithPageProgress()
    {
        var (saga, issues) = SeedSeries("Saga");
        SetLastPage(issues[1].Id, 5);
        Read(ReadingItemType.Comic, issues[1].Id, saga.Id, T0);

        var row = Assert.Single(Resolve());
        Assert.Equal(ReadingHistoryAction.Resume, row.Action);
        Assert.Equal(issues[1].Id, row.TargetId);
        Assert.Equal("page 6 of 20", row.ProgressText);
        Assert.False(row.IsFinished);
    }

    [Fact]
    public void FinishedIssue_ReadsNext_SkippingMissingFiles()
    {
        var (saga, issues) = SeedSeries("Saga", issueCount: 4);
        SetLastPage(issues[0].Id, 19);
        using (var ctx = NewContext())
        {
            ctx.Issues.Single(i => i.Id == issues[1].Id).FileIsMissing = true;
            ctx.SaveChanges();
        }

        Read(ReadingItemType.Comic, issues[0].Id, saga.Id, T0, ReadingEventKind.Finished);

        var row = Assert.Single(Resolve());
        Assert.True(row.IsFinished);
        Assert.Equal(ReadingHistoryAction.ReadNext, row.Action);
        Assert.Equal(issues[2].Id, row.TargetId);
        Assert.Equal("#3", row.TargetLabel);
        Assert.Equal("#1", row.ItemLabel);
    }

    [Fact]
    public void FinishedLastIssue_IsCaughtUp_WithNoAction()
    {
        var (saga, issues) = SeedSeries("Saga");
        SetLastPage(issues[2].Id, 19);
        Read(ReadingItemType.Comic, issues[2].Id, saga.Id, T0);

        var row = Assert.Single(Resolve());
        Assert.True(row.IsCaughtUp);
        Assert.Equal(ReadingHistoryAction.None, row.Action);
        Assert.Null(row.TargetId);
    }

    [Fact]
    public void Pdf_Opens_FinishedBook_HasNoAction()
    {
        var pdf = SeedBook("Blame", BookFormat.Pdf);
        var done = SeedBook("Piranesi", finished: true);
        Read(ReadingItemType.Novel, pdf.Id, null, T0);
        Read(ReadingItemType.Novel, done.Id, null, T0.AddHours(1));

        var rows = Resolve();
        var pdfRow = rows.Single(r => r.Title == "Blame");
        Assert.Equal(ReadingHistoryAction.Open, pdfRow.Action);
        Assert.Equal(BookFormat.Pdf, pdfRow.BookFormat);
        Assert.Null(pdfRow.ProgressText);
        var doneRow = rows.Single(r => r.Title == "Piranesi");
        Assert.Equal(ReadingHistoryAction.None, doneRow.Action);
        Assert.True(doneRow.IsFinished);
    }

    [Fact]
    public void MangaSeries_AreTaggedManga()
    {
        var (op, issues) = SeedSeries("One Piece", ContentType.Manga);
        var (saga, sagaIssues) = SeedSeries("Saga");
        Read(ReadingItemType.Comic, issues[0].Id, op.Id, T0);
        Read(ReadingItemType.Comic, sagaIssues[0].Id, saga.Id, T0);

        var rows = Resolve();
        Assert.Equal(ReadingHistoryContentKind.Manga, rows.Single(r => r.Title == "One Piece").ContentKind);
        Assert.Equal(ReadingHistoryContentKind.Comic, rows.Single(r => r.Title == "Saga").ContentKind);
    }

    // ----- stats are untouched by hiding (spec Q2, plan step 5) -----

    [Fact]
    public void Hiding_LeavesStatsLifetimeTotalsUnchanged()
    {
        var (saga, issues) = SeedSeries("Saga");
        Read(ReadingItemType.Comic, issues[0].Id, saga.Id, T0, ReadingEventKind.Finished);
        Read(ReadingItemType.Comic, issues[1].Id, saga.Id, T0.AddHours(1), ReadingEventKind.Finished);

        int FinishedCount()
        {
            using var ctx = NewContext();
            return ctx.ReadingEvents.Count(e => e.Kind == ReadingEventKind.Finished);
        }

        int before = FinishedCount();
        new ReadingEventRecorder(NewContext).HideFromHistory(null);

        Assert.Equal(before, FinishedCount());
        Assert.Equal(2, before);
    }

    // ----- perf smoke -----

    [Fact]
    public void Resolves500Groups_And20kEvents_Quickly()
    {
        using (var ctx = NewContext())
        {
            var series = Enumerable.Range(0, 500).Select(n => new Series { Name = $"S{n}" }).ToList();
            ctx.Series.AddRange(series);
            ctx.SaveChanges();
            var issues = series.SelectMany(s => Enumerable.Range(1, 10).Select(n => new Issue { SeriesId = s.Id, Number = n.ToString(), PageCount = 20 })).ToList();
            ctx.Issues.AddRange(issues);
            ctx.SaveChanges();

            var events = Enumerable.Range(0, 20_000).Select(n =>
            {
                var issue = issues[n % issues.Count];
                return new ReadingEvent
                {
                    ItemType = ReadingItemType.Comic,
                    ItemId = issue.Id,
                    SeriesId = issue.SeriesId,
                    Kind = ReadingEventKind.Opened,
                    TimestampUtc = T0.AddMinutes(n),
                };
            });
            ctx.ReadingEvents.AddRange(events);
            ctx.SaveChanges();
        }

        var sw = Stopwatch.StartNew();
        var rows = Resolve();
        sw.Stop();

        Assert.Equal(500, rows.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Resolve took {sw.Elapsed}");
    }
}
