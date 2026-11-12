namespace Paperbunkr.Data.Entities;

/// <summary>
/// A first-class named character (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-age-
/// progression-design.md - the deferred "first-class Character entity" it flags as the gap in
/// graph-driven family scoping). Auto-materialized from the free-text <see cref="Issue.Characters"/>
/// ComicInfo field by <c>CharacterResolver</c>; that string field stays the editable source of
/// truth (this is a derived index over it), so there's no editor for <see cref="Character"/> itself.
/// </summary>
public class Character : IHasEntityProfile
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Provider image link (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3); the file lives in the entity image cache.</summary>
    public string? ImageUrl { get; set; }

    public EntityImageSource ImageSource { get; set; }

    /// <summary>When the providers were last asked for this entity's profile; a miss is stamped too, a failure is not. Rechecked after 30 days.</summary>
    public DateTime? ImageCheckedAt { get; set; }

    /// <summary>The user's own image (a cached copy); wins over <see cref="ImageUrl"/>. A row with one is never pruned.</summary>
    public string? CustomImagePath { get; set; }

    /// <summary>A short provider bio (Metron <c>desc</c>, ComicVine <c>deck</c>).</summary>
    public string? Description { get; set; }

    /// <summary>Other names, comma-separated ("Ororo Munroe").</summary>
    public string? Aliases { get; set; }

    public List<CharacterAppearance> Appearances { get; set; } = new();
}
