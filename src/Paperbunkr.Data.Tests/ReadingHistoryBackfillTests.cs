using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the one-time <c>AddReadingHistoryColumns</c> name-snapshot backfill (docs/superpowers/specs/
/// 2026-09-29-insights-reading-history-design.md §1) by re-running <see cref="ReadingHistoryBackfill.Statements"/>
/// - the same statements the migration runs - against a fresh schema.
/// </summary>
public class ReadingHistoryBackfillTests : IDisposable
{
    private static readonly DateTime When = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ReadingHistoryBackfillTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_history_backfill_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
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

    private static ReadingEvent Event(ReadingItemType type, int itemId, int? seriesId) => new()
    {
        ItemType = type,
        ItemId = itemId,
        Kind = ReadingEventKind.Opened,
        TimestampUtc = When,
        SeriesId = seriesId,
    };

    private static void RunBackfill(PaperbunkrDbContext ctx)
    {
        foreach (var sql in ReadingHistoryBackfill.Statements)
        {
            ctx.Database.ExecuteSqlRaw(sql);
        }
    }

    [Fact]
    public void Backfill_FillsNamesForLiveItems_AndLeavesDeletedItemsNull()
    {
        int numbered, volumeOnly, titled, inSeries, standalone;
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Saga" };
            var bookSeries = new BookSeries { Name = "Kingkiller Chronicle" };
            ctx.Series.Add(series);
            ctx.BookSeries.Add(bookSeries);
            ctx.SaveChanges();

            var n = new Issue { SeriesId = series.Id, Number = " 12 " };
            var v = new Issue { SeriesId = series.Id, Volume = "2" };
            var t = new Issue { SeriesId = series.Id, Title = "Annual" };
            var b1 = new Book { Title = "The Name of the Wind", FilePath = "a", BookSeriesId = bookSeries.Id };
            var b2 = new Book { Title = "Piranesi", FilePath = "b" };
            ctx.Issues.AddRange(n, v, t);
            ctx.Books.AddRange(b1, b2);
            ctx.SaveChanges();
            (numbered, volumeOnly, titled, inSeries, standalone) = (n.Id, v.Id, t.Id, b1.Id, b2.Id);

            ctx.ReadingEvents.AddRange(
                Event(ReadingItemType.Comic, numbered, series.Id),
                Event(ReadingItemType.Comic, volumeOnly, series.Id),
                Event(ReadingItemType.Comic, titled, series.Id),
                Event(ReadingItemType.Comic, 9999, series.Id),          // issue deleted before the backfill
                Event(ReadingItemType.Novel, inSeries, bookSeries.Id),
                Event(ReadingItemType.Novel, standalone, null),
                Event(ReadingItemType.Novel, 8888, null));              // book deleted before the backfill
            ctx.SaveChanges();

            RunBackfill(ctx);
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var rows = ctx.ReadingEvents.AsNoTracking().ToList();
            ReadingEvent Row(ReadingItemType type, int id) => rows.Single(r => r.ItemType == type && r.ItemId == id);

            Assert.Equal(("Saga", "#12"), (Row(ReadingItemType.Comic, numbered).SeriesTitle, Row(ReadingItemType.Comic, numbered).ItemLabel));
            Assert.Equal("Vol. 2", Row(ReadingItemType.Comic, volumeOnly).ItemLabel);
            Assert.Equal("Annual", Row(ReadingItemType.Comic, titled).ItemLabel);
            Assert.Null(Row(ReadingItemType.Comic, 9999).SeriesTitle);
            Assert.Null(Row(ReadingItemType.Comic, 9999).ItemLabel);

            Assert.Equal("Kingkiller Chronicle", Row(ReadingItemType.Novel, inSeries).SeriesTitle);
            Assert.Equal("The Name of the Wind", Row(ReadingItemType.Novel, inSeries).ItemLabel);
            Assert.Equal("Piranesi", Row(ReadingItemType.Novel, standalone).SeriesTitle);
            Assert.Null(Row(ReadingItemType.Novel, standalone).ItemLabel);
            Assert.Null(Row(ReadingItemType.Novel, 8888).SeriesTitle);

            Assert.All(rows, r => Assert.False(r.HiddenFromHistory));
        }
    }

    [Fact]
    public void Backfill_IsIdempotent()
    {
        using var ctx = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Saga" };
        ctx.Series.Add(series);
        ctx.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        ctx.Issues.Add(issue);
        ctx.SaveChanges();
        ctx.ReadingEvents.Add(Event(ReadingItemType.Comic, issue.Id, series.Id));
        ctx.SaveChanges();

        RunBackfill(ctx);
        RunBackfill(ctx);

        var row = ctx.ReadingEvents.AsNoTracking().Single();
        Assert.Equal("Saga", row.SeriesTitle);
        Assert.Equal("#1", row.ItemLabel);
    }
}
