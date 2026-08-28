namespace Paperbunkr.Data.Entities;

/// <summary>
/// A persisted "don't suggest this issue for this event again" marker (docs/superpowers/specs/
/// 2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md - the persistence that
/// phase deferred). Not user content, just a nag-suppression flag: both FKs cascade so it
/// disappears cleanly when either endpoint is deleted and never blocks an issue/event deletion.
/// </summary>
public class EventSuggestionDismissal
{
    public int Id { get; set; }

    public int StoryEventId { get; set; }

    public StoryEvent? StoryEvent { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public DateTime DismissedAt { get; set; } = DateTime.UtcNow;
}
