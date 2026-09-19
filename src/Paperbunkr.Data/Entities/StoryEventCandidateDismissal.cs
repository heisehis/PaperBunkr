namespace Paperbunkr.Data.Entities;

/// <summary>
/// A persisted "don't suggest this arc as a story event again" marker for
/// <see cref="Metadata.StoryArcGroupingResolver"/> candidates (docs/superpowers/specs/2026-09-17-
/// storyevent-continuity-autopopulate-design.md). Keyed on <see cref="ArcName"/>/<see cref="Publisher"/>
/// rather than a <see cref="StoryEvent"/> id: at dismissal time no <see cref="StoryEvent"/> row
/// exists yet, since the candidate hasn't been accepted.
/// </summary>
public class StoryEventCandidateDismissal
{
    public int Id { get; set; }

    public string ArcName { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public DateTime DismissedAt { get; set; } = DateTime.UtcNow;
}
