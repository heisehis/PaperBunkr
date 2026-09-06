using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddReadingEventLog</c> migration (docs/superpowers/specs/2026-09-05-insights-
/// dashboard-design.md §4) applies cleanly on a full migrate and that the new schema round-trips.
/// Backfill correctness is covered separately by <see cref="ReadingEventBackfillTests"/>; a full
/// down-migrate is deliberately not tested here - the shared migration chain has a pre-existing
/// orphan-column rollback bug unrelated to this migration (see <c>AddReadingEventLog.Down</c>'s note).
/// </summary>
public class AddReadingEventLogMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public AddReadingEventLogMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_readevlog_mig_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
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
    public void FullMigrate_CreatesReadingEventsTable_AndBooksCharacterCountColumn()
    {
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Database.Migrate();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var book = new Book { Title = "T", FilePath = "p", CharacterCount = 54000 };
            ctx.Books.Add(book);
            ctx.SaveChanges();

            ctx.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Novel,
                ItemId = book.Id,
                Kind = ReadingEventKind.Finished,
                TimestampUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                PagesRead = 30,
            });
            ctx.SaveChanges();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.Equal(54000, ctx.Books.Single().CharacterCount);
            var ev = ctx.ReadingEvents.Single();
            Assert.Equal(ReadingItemType.Novel, ev.ItemType);
            Assert.Equal(ReadingEventKind.Finished, ev.Kind);
            Assert.Equal(30, ev.PagesRead);
        }
    }
}
