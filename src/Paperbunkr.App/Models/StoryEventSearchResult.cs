namespace Paperbunkr.App.Models;

/// <summary>One candidate <c>StoryEvent</c> shown while linking a Reading List to its official reading order (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-reading-list-overhaul-design.md).</summary>
public sealed class StoryEventSearchResult
{
    public required int StoryEventId { get; init; }
    public required string Name { get; init; }
}
