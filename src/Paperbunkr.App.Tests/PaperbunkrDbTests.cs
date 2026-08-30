using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PaperbunkrDb.CreateContext"/>'s crash-safety pragmas (docs/superpowers/
/// specs/2026-08-29-db-corruption-safeguards-design.md §1) against a temp SQLite file redirected
/// via <see cref="PaperbunkrDbContext.DatabasePathOverride"/> - the same seam
/// <see cref="BackupServiceTests"/>/<see cref="ReaderScreenViewModelTests"/> use. Joins
/// <see cref="AvaloniaTestCollection"/> for the same reason <see cref="BackupServiceTests"/> does -
/// serializes against every other test class that mutates the shared static
/// <c>DatabasePathOverride</c>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PaperbunkrDbTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public PaperbunkrDbTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pragma_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
            if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void CreateContext_SetsWalModeAndFullSynchronous()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Database.EnsureCreated();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        using var journalCmd = conn.CreateCommand();
        journalCmd.CommandText = "PRAGMA journal_mode;";
        string journalMode = (string)journalCmd.ExecuteScalar()!;
        Assert.Equal("wal", journalMode, ignoreCase: true);

        using var syncCmd = conn.CreateCommand();
        syncCmd.CommandText = "PRAGMA synchronous;";
        long synchronous = (long)syncCmd.ExecuteScalar()!;
        Assert.Equal(2, synchronous); // SQLite synchronous: 0=OFF, 1=NORMAL, 2=FULL, 3=EXTRA - "FULL" as configured here reports 2.
    }

    [Fact]
    public void CreateContext_SetsForeignKeysOn()
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();

        using var cmd = context.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys;";
        long foreignKeys = (long)cmd.ExecuteScalar()!;
        Assert.Equal(1, foreignKeys);
    }
}
