namespace Paperbunkr.Data.Entities;

/// <summary>
/// A named, ordered collection of real <see cref="Issue"/> references — new entity
/// (docs/superpowers/specs/2026-08-06-reading-lists-design.md). Matches ComicRackCE's actual
/// post-import reading-list model (<c>ComicIdListItem.BookIds</c>, confirmed by direct source
/// check): never a dangling text reference, always resolved to a real book, with unmatched import
/// rows becoming placeholder <see cref="Issue"/>s (see <see cref="Entities.Issue.IsPlaceholder"/>)
/// rather than staying unresolved.
/// </summary>
public class ReadingList
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>
    /// Forward-compat for the deferred external story-arc lookup pass (spec §1) — mirrors
    /// CBLManager's <c>ArcLink</c> sidecar-file fields, but as real columns rather than a JSON
    /// sidecar (that workaround existed only because CE's plugin API had no metadata slot on a
    /// list reachable from a plugin). All null until that pass exists.
    /// </summary>
    public string? Source { get; set; }

    public string? ArcId { get; set; }

    public string? ArcName { get; set; }

    public string? Description { get; set; }

    public string? CoverImageUrl { get; set; }

    /// <summary>Phase 4c overhaul (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-reading-list-overhaul-design.md) - classification per the source doc's §24, defaults to User for every pre-existing list.</summary>
    public ReadingListType Type { get; set; } = ReadingListType.User;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Optional link to the <see cref="Entities.StoryEvent"/> this list represents the official
    /// reading order for (source doc §51: "An event can have an official reading order... A
    /// reading list can exist without an event") - independent of <see cref="Type"/>; setting
    /// <c>Type = Event</c> doesn't require this, and this can be set without changing <see cref="Type"/>.
    /// </summary>
    public int? StoryEventId { get; set; }

    public StoryEvent? StoryEvent { get; set; }

    public List<ReadingListItem> Items { get; set; } = new();

    /// <summary>Weighted/categorized tags on the list itself (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md) - distinct from member issues' own <see cref="IssueTag"/>s.</summary>
    public List<ReadingListTag> Tags { get; set; } = new();
}
