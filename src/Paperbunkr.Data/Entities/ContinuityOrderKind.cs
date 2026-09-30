namespace Paperbunkr.Data.Entities;

/// <summary>Which order a continuity-linked reading list was built in, so Rebuild recomputes the same one
/// (docs/superpowers/specs/2026-09-28-reading-lists-build-from-events-design.md §2).</summary>
public enum ContinuityOrderKind
{
    /// <summary>As the Continuity map reads left to right: events in chronology order, issues between events by date.</summary>
    StoryOrder,

    /// <summary>Every member-series issue by release date.</summary>
    PublicationOrder,
}
