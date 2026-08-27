using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Models;

/// <summary>Sidebar row for one <c>StoryEvent</c> (docs/superpowers/specs/2026-08-17-metadata-model-phase4b-story-events-design.md) - name, member count, and whether it's the currently open event. Mirrors <see cref="ReadingListSummary"/>.</summary>
public class StoryEventSummary
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int MemberCount { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Deletes the whole event (docs/superpowers/specs/2026-08-22-delete-functionality-design.md).</summary>
    public required TwoStepConfirm DeleteConfirm { get; init; }
}
