using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the one-time <c>AddReadingEventLog</c> backfill SQL (docs/superpowers/specs/
/// 2026-09-05-insights-dashboard-design.md §4.1). Runs against a fully-migrated database with the
/// events table cleared, then re-executes <see cref="ReadingEventBackfill.Statements"/> - the same
/// statements the migration itself runs.
/// </summary>
public class ReadingEventBackfillTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ReadingEventBackfillTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_backfill_test_{Guid.NewGuid():N}.db");
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

    [Fact]
    public void Backfill_SynthesisesOpenedAndFinishedRowsFromLegacyState()
    {
        using var ctx = new PaperbunkrDbContext(_dbOptions);
        ctx.Database.ExecuteSqlRaw("DELETE FROM \"ReadingEvents\";");

        var series = new Series { Name = "S", Publisher = "Image" };
        ctx.Series.Add(series);
        ctx.SaveChanges();

        var opened = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        ctx.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", PageCount = 100, LastPageRead = 98, OpenedTime = opened, Publisher = "Image" }); // read-through
        ctx.Issues.Add(new Issue { SeriesId = series.Id, Number = "2", PageCount = 100, LastPageRead = 20, OpenedTime = opened });                    // opened only
        ctx.Issues.Add(new Issue { SeriesId = series.Id, Number = "3", PageCount = 100, LastPageRead = null });                                       // never opened

        ctx.Books.Add(new Book { Title = "Finished novel", FilePath = "a", Finished = true, LastOpenedTime = opened });
        ctx.Books.Add(new Book { Title = "Started novel", FilePath = "b", LastOpenedTime = opened });
        ctx.Books.Add(new Book { Title = "Untouched novel", FilePath = "c" });
        ctx.SaveChanges();

        foreach (var sql in ReadingEventBackfill.Statements)
        {
            ctx.Database.ExecuteSqlRaw(sql);
        }

        var events = ctx.ReadingEvents.AsNoTracking().ToList();

        // 2 issues opened + 2 novels opened = 4 Opened rows.
        Assert.Equal(4, events.Count(e => e.Kind == ReadingEventKind.Opened));
        Assert.Equal(2, events.Count(e => e.Kind == ReadingEventKind.Finished)); // 1 issue read-through + 1 finished novel
        Assert.All(events, e => Assert.Null(e.PagesRead));
        Assert.All(events, e => Assert.Equal(opened, e.TimestampUtc));
        Assert.Contains(events, e => e is { ItemType: ReadingItemType.Comic, Kind: ReadingEventKind.Finished, Publisher: "Image" });
    }
}
