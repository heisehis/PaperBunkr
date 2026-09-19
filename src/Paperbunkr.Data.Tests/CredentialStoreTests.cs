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

    [Fact]
    public void Set_StoresCiphertext_NotThePlainText()
    {
        CredentialStore.Set(_context, "qBittorrent", CredentialKind.Password, "hunter2");

        var raw = _context.ProviderCredentials.Single(c => c.Provider == "qBittorrent").Value;
        Assert.True(CredentialStore.IsEncrypted(raw));
        Assert.DoesNotContain("hunter2", raw);
        Assert.Equal("hunter2", CredentialStore.Get(_context, "qBittorrent", CredentialKind.Password));
    }

    [Fact]
    public void Get_OnLegacyPlainTextRow_ReturnsValue_AndRewritesItEncrypted()
    {
        _context.ProviderCredentials.Add(new ProviderCredential
        {
            Provider = "ComicVine",
            Kind = CredentialKind.ApiKey,
            Value = "legacy-key",
            UpdatedAt = DateTime.UtcNow,
        });
        _context.SaveChanges();

        Assert.Equal("legacy-key", CredentialStore.Get(_context, "ComicVine", CredentialKind.ApiKey));

        var raw = _context.ProviderCredentials.Single(c => c.Provider == "ComicVine").Value;
        Assert.True(CredentialStore.IsEncrypted(raw));
        Assert.Equal("legacy-key", CredentialStore.Get(_context, "ComicVine", CredentialKind.ApiKey));
    }

    [Fact]
    public void Get_OnUndecryptableValue_ReturnsNull_InsteadOfThrowing()
    {
        // Simulates a DB copied from another Windows account/machine: prefixed, but not ours.
        _context.ProviderCredentials.Add(new ProviderCredential
        {
            Provider = "Metron",
            Kind = CredentialKind.Password,
            Value = "dpapi1:" + Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 }),
            UpdatedAt = DateTime.UtcNow,
        });
        _context.SaveChanges();

        Assert.Null(CredentialStore.Get(_context, "Metron", CredentialKind.Password));
        Assert.Null(CredentialStore.Get(_context, "Metron", CredentialKind.Password));
    }

    [Fact]
    public void Get_OnNonBase64Ciphertext_ReturnsNull()
    {
        _context.ProviderCredentials.Add(new ProviderCredential
        {
            Provider = "Metron",
            Kind = CredentialKind.Username,
            Value = "dpapi1:!!!not-base64!!!",
            UpdatedAt = DateTime.UtcNow,
        });
        _context.SaveChanges();

        Assert.Null(CredentialStore.Get(_context, "Metron", CredentialKind.Username));
    }

    [Fact]
    public void Set_EmptyValue_StaysEmpty_SoHasCredentialsTreatsItAsAbsent()
    {
        CredentialStore.Set(_context, "Metron", CredentialKind.Username, "");

        Assert.False(CredentialStore.HasCredentials(_context, "Metron", CredentialKind.Username));
        Assert.Equal("", CredentialStore.Get(_context, "Metron", CredentialKind.Username));
    }

    [Fact]
    public void HasCredentials_IsTrueForEncryptedValues()
    {
        CredentialStore.Set(_context, "Metron", CredentialKind.Username, "user");
        CredentialStore.Set(_context, "Metron", CredentialKind.Password, "pass");

        Assert.True(CredentialStore.HasCredentials(_context, "Metron", CredentialKind.Username, CredentialKind.Password));
    }
}
