using System.Security.Cryptography;
using System.Text;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Credentials;

/// <summary>
/// Thin wrapper over <see cref="ProviderCredential"/> (docs/superpowers/specs/2026-08-22-cbl-
/// manager-arc-lookup-design.md §2) - the single place reading-list source adapters (and, later,
/// tracker-sync adapters) read/write their secrets, instead of touching the DbSet directly.
/// <para>
/// Values are encrypted at rest with Windows DPAPI (<see cref="DataProtectionScope.CurrentUser"/>)
/// and stored as <c>dpapi1:</c> + base64 (docs/superpowers/specs/2026-09-19-comic-acquisition-
/// daemon-design.md §4). Rows written before this upgrade hold plain text; <see cref="Get"/> reads
/// those as-is and rewrites them encrypted, so existing ComicVine/Metron/tracker keys upgrade lazily.
/// A value that no longer decrypts (a DB copied to another Windows account or machine) reads as
/// <c>null</c> - the same "not connected" state as a missing credential - instead of throwing.
/// </para>
/// </summary>
public static class CredentialStore
{
    private const string EncryptedPrefix = "dpapi1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Paperbunkr.CredentialStore.v1");

    public static string? Get(PaperbunkrDbContext context, string provider, CredentialKind kind)
    {
        var row = context.ProviderCredentials
            .FirstOrDefault(c => c.Provider == provider && c.Kind == kind);
        if (row is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(row.Value))
        {
            return row.Value;
        }

        if (!IsEncrypted(row.Value))
        {
            // Legacy plain-text row: upgrade it in place, then hand back the plain value.
            var plain = row.Value;
            row.Value = Protect(plain);
            context.SaveChanges();
            return plain;
        }

        return TryUnprotect(row.Value);
    }

    /// <summary>True when <paramref name="storedValue"/> is in the encrypted-at-rest format (used by tests and callers that must not treat ciphertext as a secret).</summary>
    public static bool IsEncrypted(string storedValue) =>
        storedValue.StartsWith(EncryptedPrefix, StringComparison.Ordinal);

    private static string Protect(string plain)
    {
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return EncryptedPrefix + Convert.ToBase64String(cipher);
    }

    private static string? TryUnprotect(string stored)
    {
        try
        {
            var cipher = Convert.FromBase64String(stored[EncryptedPrefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public static void Set(PaperbunkrDbContext context, string provider, CredentialKind kind, string value)
    {
        var stored = string.IsNullOrEmpty(value) ? value : Protect(value);
        var existing = context.ProviderCredentials
            .FirstOrDefault(c => c.Provider == provider && c.Kind == kind);
        if (existing is null)
        {
            context.ProviderCredentials.Add(new ProviderCredential
            {
                Provider = provider,
                Kind = kind,
                Value = stored,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Value = stored;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        context.SaveChanges();
    }

    public static void Delete(PaperbunkrDbContext context, string provider, CredentialKind kind)
    {
        var existing = context.ProviderCredentials
            .FirstOrDefault(c => c.Provider == provider && c.Kind == kind);
        if (existing is not null)
        {
            context.ProviderCredentials.Remove(existing);
            context.SaveChanges();
        }
    }

    /// <summary>True when every one of <paramref name="required"/> has a non-empty stored value for <paramref name="provider"/>.</summary>
    public static bool HasCredentials(PaperbunkrDbContext context, string provider, params CredentialKind[] required)
    {
        var stored = context.ProviderCredentials
            .Where(c => c.Provider == provider)
            .ToList();

        return required.All(kind => stored.Any(c => c.Kind == kind && !string.IsNullOrEmpty(c.Value)));
    }
}
