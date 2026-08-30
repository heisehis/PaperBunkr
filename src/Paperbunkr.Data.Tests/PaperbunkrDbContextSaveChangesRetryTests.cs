using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="PaperbunkrDbContext.SaveChanges(bool)"/>'s retry-on-transient-lock override
/// (a real "database is locked" <c>DbUpdateException</c> was observed reaching the Library screen's
/// UI from a keystroke-triggered settings save racing another writer) - a real SQLite file, a
/// second connection genuinely holding a write lock, not a mock.
/// </summary>
public class PaperbunkrDbContextSaveChangesRetryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public PaperbunkrDbContextSaveChangesRetryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_dbcontext_retry_test_{Guid.NewGuid():N}.db");
        // A short busy timeout (default is 30s - too slow to exercise in a test) so the lock-held
        // scenario below actually forces PaperbunkrDbContext's own retry loop to run, rather than
        // passing "for free" via Microsoft.Data.Sqlite's built-in wait.
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath};Default Timeout=1").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void SaveChanges_LockReleasedWithinRetryWindow_SucceedsWithoutThrowing()
    {
        using var lockingConnection = new SqliteConnection($"Data Source={_dbPath}");
        lockingConnection.Open();
        using (var begin = lockingConnection.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;";
            begin.ExecuteNonQuery();
        }
        using (var write = lockingConnection.CreateCommand())
        {
            // A write to the database header - acquires the same RESERVED/EXCLUSIVE lock any real
            // table write would, without needing to know this schema's actual column shape.
            write.CommandText = "PRAGMA user_version = 1;";
            write.ExecuteNonQuery();
        }

        // 1800ms of wall-clock lock hold - comfortably past the 1-second busy timeout above even
        // after the ~250-300ms of context/connection setup overhead before SaveChanges actually
        // attempts its write, so the first attempt genuinely throws SQLITE_BUSY and only a later
        // retry (after this fires) can succeed. Verified empirically: a shorter window (1200ms)
        // let the single internal busy-wait absorb the whole contention "for free," passing even
        // with the retry loop disabled - this test must fail without the fix, not just pass with it.
        var releaseLock = new Timer(_ =>
        {
            using var commit = lockingConnection.CreateCommand();
            commit.CommandText = "COMMIT;";
            commit.ExecuteNonQuery();
        }, null, dueTime: 1800, period: Timeout.Infinite);

        try
        {
            using var context = new PaperbunkrDbContext(_dbOptions);
            context.Series.Add(new Series { Name = "Written Under Contention" });

            var exception = Record.Exception(() => context.SaveChanges());

            Assert.Null(exception);
            using var verify = new PaperbunkrDbContext(_dbOptions);
            Assert.Contains(verify.Series, s => s.Name == "Written Under Contention");
        }
        finally
        {
            releaseLock.Dispose();
        }
    }

    [Fact]
    public void IsTransientLockError_NonLockDbUpdateException_ReturnsFalse()
    {
        var constraintViolation = new SqliteException("constraint failed", 19); // SQLITE_CONSTRAINT
        var wrapped = new DbUpdateException("failed", constraintViolation);

        Assert.False(PaperbunkrDbContext.IsTransientLockError(wrapped));
    }

    [Theory]
    [InlineData(5)] // SQLITE_BUSY
    [InlineData(6)] // SQLITE_LOCKED
    public void IsTransientLockError_LockErrorCodes_ReturnsTrue(int sqliteErrorCode)
    {
        var lockError = new SqliteException("database is locked", sqliteErrorCode);
        var wrapped = new DbUpdateException("failed", lockError);

        Assert.True(PaperbunkrDbContext.IsTransientLockError(wrapped));
    }
}
