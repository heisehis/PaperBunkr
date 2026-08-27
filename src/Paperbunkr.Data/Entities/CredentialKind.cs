namespace Paperbunkr.Data.Entities;

/// <summary>
/// The kind of secret one <see cref="ProviderCredential"/> row holds (docs/superpowers/specs/
/// 2026-08-22-cbl-manager-arc-lookup-design.md §2).
/// </summary>
public enum CredentialKind
{
    ApiKey,
    Username,
    Password,
    OAuthAccessToken,
    OAuthRefreshToken,

    /// <summary>Appended, not inserted - <see cref="ProviderCredential.Kind"/> is stored as a plain
    /// int (confirmed via its migration: no <c>HasConversion&lt;string&gt;()</c> exists for it,
    /// unlike most other enums in this codebase), so reordering existing members would silently
    /// reinterpret already-stored rows. New values must always go at the end.</summary>
    OAuthClientId,
    OAuthClientSecret,
}
