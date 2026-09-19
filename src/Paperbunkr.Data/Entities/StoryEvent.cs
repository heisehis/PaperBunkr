namespace Paperbunkr.Data.Entities;

/// <summary>
/// A named cross-series publishing/story event ("Rise of the Third Army", "Secret Wars") -
/// greenfield entity, no CE precedent (docs/superpowers/specs/2026-08-17-metadata-model-phase4b-
/// story-events-design.md). Distinct from <see cref="ReadingList"/>: order is useful but optional
/// here, and <see cref="EventMembershipRole"/> is the point of the feature, not an afterthought
/// (source doc §51).
/// </summary>
public class StoryEvent
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// ComicVine's story-arc resource id, set when a <see cref="Metadata.StoryArcGroupingResolver"/>
    /// candidate was verified against ComicVine (docs/superpowers/specs/2026-09-17-storyevent-
    /// continuity-autopopulate-design.md). Null when never verified or verified only against Metron.
    /// </summary>
    public string? ComicVineArcId { get; set; }

    /// <summary>Metron's arc resource id, same posture as <see cref="ComicVineArcId"/>.</summary>
    public string? MetronArcId { get; set; }

    public List<EventMembership> Members { get; set; } = new();
}
