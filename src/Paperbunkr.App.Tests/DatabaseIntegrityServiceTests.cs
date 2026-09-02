using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="DatabaseIntegrityService.CheckIntegrity"/> (docs/superpowers/specs/
/// 2026-08-29-db-corruption-safeguards-design.md §3) against a temp SQLite file redirected via
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> - same seam as <see cref="PaperbunkrDbTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DatabaseIntegrityServiceTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public DatabaseIntegrityServiceTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_integrity_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
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
    public void CheckIntegrity_ReturnsTrue_WhenDatabaseFileDoesNotExistYet()
    {
        bool ok = DatabaseIntegrityService.CheckIntegrity(out string? detail);

        Assert.True(ok);
        Assert.Null(detail);
    }

    [Fact]
    public void CheckIntegrity_ReturnsTrue_ForACleanDatabase()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Database.EnsureCreated();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        bool ok = DatabaseIntegrityService.CheckIntegrity(out string? detail);

        Assert.True(ok);
        Assert.Equal("ok", detail);
    }

    [Fact]
    public void CheckIntegrity_ReturnsFalse_ForACorruptedDatabaseFile()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Database.EnsureCreated();

            // EnsureCreated() alone produces a schema-only file: every table's root page is an
            // empty leaf page, so most of the file is unallocated free space rather than real
            // b-tree content. PRAGMA integrity_check only walks bytes that are actually in use
            // (cell pointers/payloads); a byte flip landing in that free space is invisible to it
            // regardless of where in the file it lands. Seed real rows so the corrupted region is
            // very likely to hit genuine page structure (confirmed empirically: the exact same
            // midpoint flip against a schema-only file leaves integrity_check reporting "ok").
            for (int i = 0; i < 500; i++)
            {
                context.Series.Add(new Series { Name = $"Series {i}", Summary = new string('x', 2000) });
            }
            context.SaveChanges();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Overwrite a chunk in the middle of the file with garbage bytes, past the header, to
        // simulate real page-level corruption rather than an unreadable/truncated file.
        byte[] bytes = File.ReadAllBytes(_dbPath);
        int start = bytes.Length / 2;
        int length = Math.Min(512, bytes.Length - start);
        for (int i = 0; i < length; i++)
        {
            bytes[start + i] = 0xFF;
        }
        File.WriteAllBytes(_dbPath, bytes);

        bool ok = DatabaseIntegrityService.CheckIntegrity(out string? detail);

        Assert.False(ok);
        Assert.NotNull(detail);
        Assert.NotEqual("ok", detail);
    }
}
