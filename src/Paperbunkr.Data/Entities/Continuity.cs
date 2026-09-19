namespace Paperbunkr.Data.Entities;

/// <summary>
/// A named fictional universe/timeline ("Earth-616", "DC Prime Earth") that any number of
/// <see cref="Series"/> can belong to at once (docs/superpowers/specs/2026-08-17-metadata-model-
/// phase4a-continuity-design.md). Distinct from <see cref="MediaRelation"/>'s
/// <c>SameContinuity</c>/<c>SharedUniverse</c> types, which are pairwise assertions between two
/// specific series - this is a first-class grouping so "what else is in this continuity" is one
/// query instead of an all-pairs relation web. M:M with <see cref="Series"/> through the explicit
/// <see cref="ContinuityMembership"/> join entity (it carries a per-membership note and sort order).
/// </summary>
public class Continuity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Publisher { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Wikidata QID for this universe (e.g. "Q2246088" for Earth-616), set when a
    /// <see cref="Metadata.ContinuityWikidataMatchResolver"/> suggestion was accepted
    /// (docs/superpowers/specs/2026-09-17-storyevent-continuity-autopopulate-design.md). Null for
    /// continuities created by hand or before this feature existed.
    /// </summary>
    public string? WikidataId { get; set; }

    /// <summary>
    /// A fan-wiki-sourced designation (e.g. "Earth-928") for a universe with no real Wikidata item
    /// of its own - confirmed most numbered Marvel/DC alternate Earths simply don't exist on
    /// Wikidata (Earth-928, Earth-58163 checked directly, absent). Kept as a genuinely separate
    /// field from <see cref="WikidataId"/> rather than overloading it, so that field keeps meaning
    /// "a real, independently-verifiable Wikidata identifier" and never silently becomes a
    /// non-Wikidata string. Exactly one of the two is ever set by
    /// <see cref="Metadata.ContinuityWikidataMatchResolver"/>.
    /// </summary>
    public string? FandomKey { get; set; }

    /// <summary>Join rows to the member <see cref="Series"/>, each with its own note and sort order.</summary>
    public List<ContinuityMembership> Memberships { get; set; } = new();
}
