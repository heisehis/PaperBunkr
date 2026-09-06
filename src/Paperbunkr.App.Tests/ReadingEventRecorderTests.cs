using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
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
}
