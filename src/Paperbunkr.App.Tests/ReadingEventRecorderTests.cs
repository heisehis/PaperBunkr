using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.History;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReadingEventRecorder"/> (docs/superpowers/specs/2026-09-05-insights-
/// dashboard-design.md §5) against a temp SQLite database.
/// </summary>
public class ReadingEventRecorderTests : IDisposable
{
    private readonly string _dbPath;

    public ReadingEventRecorderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_recorder_test_{Guid.NewGuid():N}.db");
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
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void RecordOpened_InsertsOneRow_AndRaisesTheEvent()
    {
        int raised = 0;
        var recorder = new ReadingEventRecorder(NewContext);
        recorder.ReadingEventRecorded += () => raised++;

        recorder.RecordOpened(ReadingItemType.Comic, 42, seriesId: 7, publisher: "Image", primaryGenre: "Sci-Fi");

        using var ctx = NewContext();
        var row = Assert.Single(ctx.ReadingEvents);
        Assert.Equal(ReadingEventKind.Opened, row.Kind);
        Assert.Equal(42, row.ItemId);
        Assert.Equal("Image", row.Publisher);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void RecordFinished_AlwaysInsertsANewRow_SoRereadsAccumulate()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        recorder.RecordFinished(ReadingItemType.Comic, 1, null, null, null, pagesRead: 30);
        recorder.RecordFinished(ReadingItemType.Comic, 1, null, null, null, pagesRead: 30);

        using var ctx = NewContext();
        Assert.Equal(2, ctx.ReadingEvents.Count(e => e.Kind == ReadingEventKind.Finished));
    }

    [Fact]
    public void RecordFinished_NormalisesNonPositivePagesToNull()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        recorder.RecordFinished(ReadingItemType.Novel, 5, null, null, null, pagesRead: 0);

        using var ctx = NewContext();
        Assert.Null(Assert.Single(ctx.ReadingEvents).PagesRead);
    }

    [Fact]
    public void UpdateSessionPages_FillsTheLatestOpenOpenedRow_LeavesEarlierOnesAlone()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        recorder.RecordOpened(ReadingItemType.Comic, 1, null, null, null);
        System.Threading.Thread.Sleep(5);
        recorder.RecordOpened(ReadingItemType.Comic, 1, null, null, null);

        recorder.UpdateSessionPages(ReadingItemType.Comic, 1, 12);

        using var ctx = NewContext();
        var rows = ctx.ReadingEvents.OrderBy(e => e.Id).ToList();
        Assert.Null(rows[0].PagesRead);
        Assert.Equal(12, rows[1].PagesRead);
    }

    [Fact]
    public void UpdateSessionPages_IsANoOp_WhenNoOpenSessionRowExists()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        recorder.UpdateSessionPages(ReadingItemType.Comic, 99, 5); // nothing to update

        using var ctx = NewContext();
        Assert.Empty(ctx.ReadingEvents);
    }

    // ===== Insights History (docs/superpowers/specs/2026-09-29-insights-reading-history-design.md §1) =====

    [Fact]
    public void Record_SnapshotsHistoryNames_ForComicsBookSeriesAndStandaloneBooks()
    {
        int issueId, seriesBookId, standaloneId, seriesId, bookSeriesId;
        using (var ctx = NewContext())
        {
            var series = new Series { Name = "Saga" };
            var bookSeries = new BookSeries { Name = "Kingkiller Chronicle" };
            ctx.AddRange(series, bookSeries);
            ctx.SaveChanges();
            var issue = new Issue { SeriesId = series.Id, Number = "4" };
            var inSeries = new Book { Title = "The Wise Man's Fear", FilePath = "a", BookSeriesId = bookSeries.Id };
            var standalone = new Book { Title = "Piranesi", FilePath = "b" };
            ctx.AddRange(issue, inSeries, standalone);
            ctx.SaveChanges();
            (issueId, seriesBookId, standaloneId, seriesId, bookSeriesId) = (issue.Id, inSeries.Id, standalone.Id, series.Id, bookSeries.Id);
        }

        var recorder = new ReadingEventRecorder(NewContext);
        recorder.RecordOpened(ReadingItemType.Comic, issueId, seriesId, null, null);
        recorder.RecordFinished(ReadingItemType.Novel, seriesBookId, bookSeriesId, null, null, 40);
        recorder.RecordOpened(ReadingItemType.Novel, standaloneId, null, null, null);

        using var check = NewContext();
        var rows = check.ReadingEvents.OrderBy(e => e.Id).ToList();
        Assert.Equal(("Saga", "#4"), (rows[0].SeriesTitle, rows[0].ItemLabel));
        Assert.Equal(("Kingkiller Chronicle", "The Wise Man's Fear"), (rows[1].SeriesTitle, rows[1].ItemLabel));
        Assert.Equal(("Piranesi", (string?)null), (rows[2].SeriesTitle, rows[2].ItemLabel));
    }

    [Fact]
    public void Record_StillInserts_WithNullNames_WhenTheItemIsGone()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        recorder.RecordOpened(ReadingItemType.Comic, 404, 7, null, null);

        using var ctx = NewContext();
        var row = Assert.Single(ctx.ReadingEvents);
        Assert.Null(row.SeriesTitle);
        Assert.Null(row.ItemLabel);
    }

    [Fact]
    public void Record_SnapshotsRemoteLibraryItems_DespiteTheRemoteQueryFilter()
    {
        int issueId;
        using (var ctx = NewContext())
        {
            var source = new RemoteSource { InstanceId = "peer", DisplayName = "Peer", Host = "10.0.0.2" };
            ctx.Add(source);
            ctx.SaveChanges();
            var series = new Series { Name = "Remote Saga", RemoteSourceId = source.Id };
            ctx.Add(series);
            ctx.SaveChanges();
            var issue = new Issue { SeriesId = series.Id, Number = "1", RemoteSourceId = source.Id };
            ctx.Add(issue);
            ctx.SaveChanges();
            issueId = issue.Id;
        }

        new ReadingEventRecorder(NewContext).RecordOpened(ReadingItemType.Comic, issueId, null, null, null);

        using var check = NewContext();
        Assert.Equal("Remote Saga", Assert.Single(check.ReadingEvents).SeriesTitle);
    }

    [Fact]
    public void HideFromHistory_Group_HidesOnlyThatGroup_KeyedByItemType()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        recorder.RecordOpened(ReadingItemType.Comic, 1, 5, null, null);   // comic series 5
        recorder.RecordOpened(ReadingItemType.Novel, 2, 5, null, null);   // book series 5 - same integer, other group
        recorder.RecordOpened(ReadingItemType.Comic, 3, 6, null, null);   // comic series 6
        recorder.RecordOpened(ReadingItemType.Novel, 4, null, null, null); // standalone book 4
        int raised = 0;
        recorder.ReadingEventRecorded += () => raised++;

        recorder.HideFromHistory(new ReadingHistoryGroupKey(ReadingHistoryGroupKind.ComicSeries, 5));
        recorder.HideFromHistory(new ReadingHistoryGroupKey(ReadingHistoryGroupKind.Book, 4));

        using var ctx = NewContext();
        var hidden = ctx.ReadingEvents.Where(e => e.HiddenFromHistory).Select(e => e.ItemId).OrderBy(id => id).ToList();
        Assert.Equal(new[] { 1, 4 }, hidden);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void HideFromHistory_Null_HidesEverything_AndLaterReadsStayVisible()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        recorder.RecordOpened(ReadingItemType.Comic, 1, 5, null, null);
        recorder.RecordOpened(ReadingItemType.Novel, 2, null, null, null);

        recorder.HideFromHistory(null);
        recorder.RecordOpened(ReadingItemType.Comic, 1, 5, null, null);

        using var ctx = NewContext();
        var rows = ctx.ReadingEvents.OrderBy(e => e.Id).ToList();
        Assert.True(rows[0].HiddenFromHistory);
        Assert.True(rows[1].HiddenFromHistory);
        Assert.False(rows[2].HiddenFromHistory);
    }
}
