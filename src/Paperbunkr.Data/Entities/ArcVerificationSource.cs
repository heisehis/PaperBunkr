namespace Paperbunkr.Data.Entities;

/// <summary>External arc-lookup source consulted by <see cref="Metadata.ArcExternalVerificationService"/>.</summary>
public enum ArcVerificationSource
{
    ComicVine,
    Metron,

    /// <summary>A Wikidata item lookup for the smart connector (docs/superpowers/specs/2026-09-27-continuity-map-design.md §1).</summary>
    Wikidata,
}
