using Paperbunkr.App.Services;
using Paperbunkr.Data;

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
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Overwrite the cell-content area of page 1 (the sqlite_master b-tree root, which every
        // SQLite file has) with garbage bytes, past the 100-byte file header, to simulate real
        // page-level corruption rather than an unreadable/truncated file. Deliberately NOT
        // "bytes.Length / 2" - that midpoint's page depends on however many pages the current
        // schema happens to occupy, so as the model grows/shrinks the corruption can silently
        // drift into a freelist/unallocated page that integrity_check doesn't flag, making this
        // test flake without any bug in the production code. Page 1 always holds real btree
        // content, so corrupting it is schema-size-independent.
        byte[] bytes = File.ReadAllBytes(_dbPath);
        int pageSize = (bytes[16] << 8) | bytes[17];
        if (pageSize == 1) pageSize = 65536; // header encodes 65536 as 0x0001 (can't fit in u16)
        int start = 100;
        int length = Math.Min(pageSize - start, bytes.Length - start);
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
