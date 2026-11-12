namespace Paperbunkr.Data.Entities;

/// <summary>
/// A provider's issue list for a story event's verified arc, saved so the map can show the issues the library doesn't own
/// (docs/superpowers/specs/2026-11-11-continuity-map-completeness-design.md §1). Display evidence only: it never adds, removes or reorders
/// memberships. One row per event and provider; a new fetch replaces it.
/// </summary>
public class EventArcSnapshot
{
    public int Id { get; set; }

    public int StoryEventId { get; set; }

    public StoryEvent? StoryEvent { get; set; }

    /// <summary>"Metron" or "ComicVine".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider arc id the list came from.</summary>
    public string ArcId { get; set; } = string.Empty;

    public DateTime FetchedAt { get; set; }

    /// <summary>The provider's own count, when it reports one.</summary>
    public int? ReportedCount { get; set; }

    /// <summary>False when paging stopped early or the provider skipped lines.</summary>
    public bool IsComplete { get; set; }

    /// <summary>The ordered issue list as JSON (<c>EventArcEntry[]</c>).</summary>
    public string IssuesJson { get; set; } = "[]";
}
