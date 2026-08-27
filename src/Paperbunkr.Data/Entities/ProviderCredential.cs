namespace Paperbunkr.Data.Entities;

/// <summary>
/// A provider-keyed secret (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §2) -
/// a shared, reusable credential store rather than one-off named columns on <see cref="AppSettings"/>
/// per provider, since the deferred AniList tracker-sync pass will need OAuth tokens later and this
/// pass needs ComicVine/Metron credentials now. Stored plaintext, same trust model as every other
/// setting in this single-user local SQLite database - no vault exists anywhere else in this codebase.
/// </summary>
public class ProviderCredential
{
    public int Id { get; set; }

    /// <summary>Free-form provider key, e.g. "ComicVine", "Metron" - not tied to <see cref="ExternalMetadataProvider"/>, whose scope is series-metadata providers, a different concern.</summary>
    public string Provider { get; set; } = string.Empty;

    public CredentialKind Kind { get; set; }

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }
}
