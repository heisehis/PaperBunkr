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

    public List<ReadingListItem> Items { get; set; } = new();
}
