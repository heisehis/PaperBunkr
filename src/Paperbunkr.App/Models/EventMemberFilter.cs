namespace Paperbunkr.App.Models;

/// <summary>The event reading list's filter chips (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Event page").
/// The list always stays in reading order; a filter only hides rows.</summary>
public enum EventMemberFilter
{
    All,
    Core,
    HideOptional,
    Unread,
    NeedsReview,
}
