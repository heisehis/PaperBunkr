using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>Exercises <see cref="CredentialStore"/> (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §2).</summary>
public class CredentialStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public CredentialStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_credentialstore_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
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
    public void Get_ReturnsNullWhenNothingStored()
    {
        Assert.Null(CredentialStore.Get(_context, "ComicVine", CredentialKind.ApiKey));
    }

    [Fact]
    public void Set_ThenGet_RoundTrips()
    {
        CredentialStore.Set(_context, "ComicVine", CredentialKind.ApiKey, "abc123");
        Assert.Equal("abc123", CredentialStore.Get(_context, "ComicVine", CredentialKind.ApiKey));
    }

    [Fact]
    public void Set_CalledTwice_UpdatesInPlaceRatherThanDuplicating()
    {
        CredentialStore.Set(_context, "Metron", CredentialKind.Password, "first");
        CredentialStore.Set(_context, "Metron", CredentialKind.Password, "second");

        Assert.Equal("second", CredentialStore.Get(_context, "Metron", CredentialKind.Password));
        Assert.Equal(1, _context.ProviderCredentials.Count(c => c.Provider == "Metron" && c.Kind == CredentialKind.Password));
    }

    [Fact]
    public void Delete_RemovesStoredValue()
    {
        CredentialStore.Set(_context, "ComicVine", CredentialKind.ApiKey, "abc123");
        CredentialStore.Delete(_context, "ComicVine", CredentialKind.ApiKey);
        Assert.Null(CredentialStore.Get(_context, "ComicVine", CredentialKind.ApiKey));
    }

    [Fact]
    public void DifferentProvidersAndKinds_DoNotCollide()
    {
        CredentialStore.Set(_context, "Metron", CredentialKind.Username, "alice");
        CredentialStore.Set(_context, "Metron", CredentialKind.Password, "hunter2");
        CredentialStore.Set(_context, "ComicVine", CredentialKind.ApiKey, "abc123");

        Assert.Equal("alice", CredentialStore.Get(_context, "Metron", CredentialKind.Username));
        Assert.Equal("hunter2", CredentialStore.Get(_context, "Metron", CredentialKind.Password));
        Assert.Equal("abc123", CredentialStore.Get(_context, "ComicVine", CredentialKind.ApiKey));
    }

    [Fact]
    public void HasCredentials_FalseUntilEveryRequiredKindIsStored()
    {
        Assert.False(CredentialStore.HasCredentials(_context, "Metron", CredentialKind.Username, CredentialKind.Password));

        CredentialStore.Set(_context, "Metron", CredentialKind.Username, "alice");
        Assert.False(CredentialStore.HasCredentials(_context, "Metron", CredentialKind.Username, CredentialKind.Password));

        CredentialStore.Set(_context, "Metron", CredentialKind.Password, "hunter2");
        Assert.True(CredentialStore.HasCredentials(_context, "Metron", CredentialKind.Username, CredentialKind.Password));
    }
}
