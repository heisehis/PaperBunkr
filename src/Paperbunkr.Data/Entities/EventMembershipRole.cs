namespace Paperbunkr.Data.Entities;

/// <summary>
/// An issue's role within a <see cref="StoryEvent"/> (docs/superpowers/specs/2026-08-17-metadata-
/// model-phase4b-story-events-design.md §50), straight from the source doc's own worked example -
/// also reused by <see cref="ReadingListItem.Role"/> (Phase 4c), which has no distinct vocabulary
/// of its own.
/// </summary>
public enum EventMembershipRole
{
    Prologue,
    Core,
    TieIn,
    Epilogue,
    Optional,
    Aftermath,
}
