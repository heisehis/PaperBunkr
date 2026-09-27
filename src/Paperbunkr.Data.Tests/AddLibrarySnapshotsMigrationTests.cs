using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddLibrarySnapshots</c> migration (docs/superpowers/specs/2026-09-22-insights-
/// backlog-burndown-design.md) applies cleanly on a full migrate and that the new schema round-trips,
/// including the <c>SnapshotDate</c> unique index. A full down-migrate is deliberately not tested here,
/// same reasoning as <see cref="AddReadingEventLogMigrationTests"/> - the shared migration chain has a
/// pre-existing orphan-column rollback bug unrelated to this migration.
/// </summary>
public class AddLibrarySnapshotsMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public AddLibrarySnapshotsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_libsnap_mig_{Guid.NewGuid():N}.db");
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
    public void FullMigrate_CreatesLibrarySnapshotsTable_AndRoundTripsARow()
    {
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Database.Migrate();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.LibrarySnapshots.Add(new LibrarySnapshot
            {
                SnapshotDate = new DateOnly(2026, 9, 22),
                TotalOwnedComics = 120,
                BacklogComics = 40,
            });
            ctx.SaveChanges();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var row = ctx.LibrarySnapshots.Single();
            Assert.Equal(new DateOnly(2026, 9, 22), row.SnapshotDate);
            Assert.Equal(120, row.TotalOwnedComics);
            Assert.Equal(40, row.BacklogComics);
        }
    }

    [Fact]
    public void SnapshotDate_IsUnique()
    {
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Database.Migrate();
        }

        using var context = new PaperbunkrDbContext(_dbOptions);
        context.LibrarySnapshots.Add(new LibrarySnapshot { SnapshotDate = new DateOnly(2026, 9, 22) });
        context.SaveChanges();

        context.LibrarySnapshots.Add(new LibrarySnapshot { SnapshotDate = new DateOnly(2026, 9, 22) });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }
}
