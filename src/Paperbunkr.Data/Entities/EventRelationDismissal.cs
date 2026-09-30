namespace Paperbunkr.Data.Entities;

/// <summary>
/// "These two events aren't connected" (docs/superpowers/specs/2026-09-27-continuity-map-design.md §1): written when you delete an
/// inferred or Wikidata relation, or dismiss a connector suggestion. Stored in id order; the smart connector never proposes the pair again.
/// </summary>
public class EventRelationDismissal
{
    public int Id { get; set; }

    public int LowerEventId { get; set; }

    public StoryEvent? LowerEvent { get; set; }

    public int HigherEventId { get; set; }

    public StoryEvent? HigherEvent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
