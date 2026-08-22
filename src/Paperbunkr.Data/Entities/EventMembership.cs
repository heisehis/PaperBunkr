namespace Paperbunkr.Data.Entities;

/// <summary>
/// One <see cref="Issue"/>'s ordered membership in a <see cref="StoryEvent"/> (docs/superpowers/
/// specs/2026-08-17-metadata-model-phase4b-story-events-design.md). Issue-scoped, not Series-scoped
/// like <see cref="MediaRelation"/>/<c>SeriesContinuity</c> - an event's own worked example (§50)
/// names specific issues with different roles within the same series, which a per-series membership
/// couldn't express.
/// </summary>
public class EventMembership
{
    public int Id { get; set; }

    public int StoryEventId { get; set; }

    public StoryEvent? StoryEvent { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public int Position { get; set; }

    public EventMembershipRole Role { get; set; }
}
