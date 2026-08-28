namespace Paperbunkr.Data.Entities;

/// <summary>
/// A first-class <see cref="StoryEvent"/>-to-<see cref="StoryEvent"/> connection (docs/superpowers/
/// specs/2026-08-27-metadata-model-phase4d-event-relations-design.md) - the same thing
/// <see cref="MediaRelation"/> gave series, one level up: "this event follows/crosses over with/
/// shares a universe with that event" (e.g. Secret Wars 2015 continuing Secret Wars 1984). CE has
/// no cross-series-event concept at all, let alone an event-to-event relation - this is greenfield.
///
/// Deliberately the same shape as <see cref="MediaRelation"/>, reusing <see cref="Entities.RelationType"/>
/// and <see cref="RelationEvidenceProvider"/> wholesale rather than declaring near-identical
/// duplicates. Only the creation-UI picker is scoped down to the subset that describes a
/// relationship between two events (see <c>EventsScreenViewModel</c>) - the enum itself isn't split.
/// </summary>
public class EventRelation
{
    public int Id { get; set; }

    public int SourceEventId { get; set; }

    public StoryEvent? SourceEvent { get; set; }

    public int TargetEventId { get; set; }

    public StoryEvent? TargetEvent { get; set; }

    public RelationType RelationType { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<EventRelationEvidence> Evidence { get; set; } = new();
}
