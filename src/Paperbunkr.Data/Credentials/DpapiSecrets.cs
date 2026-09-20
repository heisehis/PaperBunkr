using System.Security.Cryptography;
using System.Text;

namespace Paperbunkr.Data.Credentials;

/// <summary>
/// Windows DPAPI protection for secrets stored in the database, shared by <see cref="CredentialStore"/>
/// (provider API keys) and plugin <c>secret</c> settings (docs/superpowers/specs/2026-09-20-plugin-api-4-1-
/// design.md §6.2). Values are protected with <see cref="DataProtectionScope.CurrentUser"/> and stored as
/// <see cref="Prefix"/> + base64.
/// <para>
/// What this does and doesn't give you: it protects data <b>at rest</b> - a copied database file or
/// settings dump - and it is bound to one Windows user on one machine, so a value copied to another user
/// or machine no longer decrypts (<see cref="TryUnprotect"/> returns null). It does <b>not</b> protect
/// against code running as the same user; a native plugin is full-trust and can unprotect its own values.
/// </para>
/// <para>
/// Each store passes its own <c>entropy</c>, so ciphertext from one store can't be replayed into another.
/// <c>ProtectedData</c> is Windows-only; <see cref="IsSupported"/> is false elsewhere and callers must
/// fail closed (report the secret as unavailable) rather than fall back to storing plain text.
/// </para>
/// </summary>
public static class DpapiSecrets
{
    public const string Prefix = "dpapi1:";

    /// <summary>False off Windows, where DPAPI doesn't exist.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>True when <paramref name="stored"/> is in the protected-at-rest format (so a caller doesn't mistake ciphertext for a plain value).</summary>
    public static bool IsProtected(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Encrypts <paramref name="plain"/>. Throws <see cref="PlatformNotSupportedException"/> when <see cref="IsSupported"/> is false.</summary>
    public static string Protect(string plain, byte[] entropy)
    {
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(cipher);
    }

    /// <summary>Decrypts a value produced by <see cref="Protect"/>; null if it doesn't decrypt (wrong user/machine, corrupt, wrong entropy, or DPAPI unavailable) - never throws.</summary>
    public static string? TryUnprotect(string stored, byte[] entropy)
    {
        if (!IsProtected(stored))
        {
            return null;
        }

        try
        {
            var cipher = Convert.FromBase64String(stored[Prefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(cipher, entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
