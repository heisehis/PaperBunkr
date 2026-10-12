using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="DataChangeStamp"/> (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md §4.6). The counter is
/// process-wide and other test classes write in parallel, so these assert that it moved, never that it stayed still; what must
/// not move it is covered through <see cref="DataChangeStamp.IsWrite"/>.
/// </summary>
public class DataChangeStampTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public DataChangeStampTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_change_stamp_test_{Guid.NewGuid():N}.db");
        _context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("SELECT \"s\".\"Id\" FROM \"Series\" AS \"s\"", false)]
    [InlineData("\r\n  select 1", false)]
    [InlineData("PRAGMA journal_mode = 'WAL';", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("INSERT INTO \"Series\" (\"Name\") VALUES (@p0) RETURNING \"Id\";", true)]
    [InlineData("UPDATE \"Issues\" SET \"Rating\" = 5", true)]
    [InlineData("DELETE FROM \"Issues\"", true)]
    [InlineData("WITH x AS (SELECT 1) DELETE FROM \"Issues\"", true)]
    [InlineData("INSERT INTO \"ActivityRuns\" (\"FinishedUtc\", \"Title\") VALUES (@p0, @p1) RETURNING \"Id\";", false)]
    [InlineData("INSERT INTO \"ActivityRuns\" (\"Title\") VALUES (@p0); UPDATE \"Issues\" SET \"Rating\" = 5;", true)]
    [InlineData("DELETE FROM \"ActivityRuns\"", true)]
    [InlineData("INSERT INTO \"SeriesActivityEvents\" (\"Detail\", \"Kind\") VALUES (@p0, @p1) RETURNING \"Id\";", false)]
    [InlineData("UPDATE \"TrackingLinks\" SET \"LastSyncedAt\" = @p0 WHERE \"Id\" = @p1 RETURNING 1;", false)]
    [InlineData("UPDATE \"TrackingLinks\" SET \"LastSyncedAt\" = @p0\r\nWHERE \"Id\" = @p1\r\nRETURNING 1;", false)]   // as EF writes it
    [InlineData("INSERT INTO \"ActivityRuns\" (\"FinishedUtc\", \"Title\")\r\nVALUES (@p0, @p1)\r\nRETURNING \"Id\";", false)]
    [InlineData("UPDATE \"TrackingLinks\" SET \"LastSyncedAt\" = @p0, \"Progress\" = @p1 WHERE \"Id\" = @p2 RETURNING 1;", true)]
    public void IsWrite_TreatsEverythingButSelectAndPragmaAsAWrite(string? sql, bool expected) =>
        Assert.Equal(expected, DataChangeStamp.IsWrite(sql));

    [Fact]
    public void SaveChanges_MovesTheStamp()
    {
        long before = DataChangeStamp.Current;

        _context.Series.Add(new Series { Name = "Spawn" });
        _context.SaveChanges();

        Assert.True(DataChangeStamp.Current > before);
    }

    [Fact]
    public void BulkUpdate_ThatBypassesTheChangeTracker_MovesTheStamp()
    {
        _context.Series.Add(new Series { Name = "Spawn" });
        _context.SaveChanges();
        long before = DataChangeStamp.Current;

        _context.Series.ExecuteUpdate(s => s.SetProperty(x => x.Name, "Spawn (1992)"));

        Assert.True(DataChangeStamp.Current > before);
    }

    [Fact]
    public void RawSql_MovesTheStamp()
    {
        long before = DataChangeStamp.Current;

        _context.Database.ExecuteSqlRaw("DELETE FROM \"Series\"");

        Assert.True(DataChangeStamp.Current > before);
    }

    [Fact]
    public void CommittingATransaction_MovesTheStamp_AfterTheWritesInsideIt()
    {
        using var transaction = _context.Database.BeginTransaction();
        _context.Series.Add(new Series { Name = "Spawn" });
        _context.SaveChanges();
        long afterWrite = DataChangeStamp.Current;

        transaction.Commit();

        Assert.True(DataChangeStamp.Current > afterWrite);
    }

    [Fact]
    public void Touch_MovesTheStamp()
    {
        long before = DataChangeStamp.Current;

        DataChangeStamp.Touch();

        Assert.True(DataChangeStamp.Current > before);
    }
}
