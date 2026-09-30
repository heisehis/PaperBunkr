namespace Paperbunkr.Data.Entities;

/// <summary>
/// "Not the same" on a possible-duplicate pair of story events (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §1).
/// Stored in id order so the pair is the same both ways; the resolver never proposes or merges it again.
/// </summary>
public class StoryEventDuplicateDismissal
{
    public int Id { get; set; }

    public int LowerEventId { get; set; }

    public StoryEvent? LowerEvent { get; set; }

    public int HigherEventId { get; set; }

    public StoryEvent? HigherEvent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
