namespace Paperbunkr.Data.Entities;

/// <summary>Where a <see cref="EventOrderEvidence"/> row came from (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4).</summary>
public enum EventOrderProvider
{
    /// <summary>Comic Book Reading Orders' event timeline: the event's position in a curated story order (Marvel or DC).</summary>
    Cbro = 0,

    /// <summary>A Metron EVENT reading list: its <c>previous</c>/<c>next</c> lists, i.e. the event before and after it in a reading order.</summary>
    MetronList = 1,
}

/// <summary>
/// What an outside reading-order source said about where one story event sits (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4).
/// One row per (event, provider); a row whose <see cref="SourceId"/> is null records "looked, found nothing" so the lookup is not repeated every run.
/// CBRO rows carry <see cref="ListKey"/> + <see cref="Rank"/> (ranks only compare within one list); Metron rows carry the neighbouring lists' names.
/// </summary>
public class EventOrderEvidence
{
    public int Id { get; set; }

    public int StoryEventId { get; set; }

    public StoryEvent? StoryEvent { get; set; }

    public EventOrderProvider Provider { get; set; }

    /// <summary>The matched CBRO page URL or Metron reading-list id; null = looked up and not found.</summary>
    public string? SourceId { get; set; }

    /// <summary>The matched list's own name, for the "why" text.</summary>
    public string? SourceName { get; set; }

    /// <summary>CBRO only: which timeline ("marvel" / "dc") <see cref="Rank"/> is a position in.</summary>
    public string? ListKey { get; set; }

    /// <summary>CBRO only: 1-based position in that timeline.</summary>
    public int? Rank { get; set; }

    /// <summary>Metron only: the reading list that comes before this one.</summary>
    public string? PreviousName { get; set; }

    /// <summary>Metron only: the reading list that comes after this one.</summary>
    public string? NextName { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
