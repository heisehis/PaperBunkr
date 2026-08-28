using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Models;

/// <summary>
/// Sidebar row for one <c>Continuity</c> in the Story Events screen's Continuities mode
/// (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-browse-design.md) -
/// mirrors <see cref="StoryEventSummary"/>'s shape.
/// </summary>
public sealed record ContinuitySummary(int Id, string Name, string? Publisher, int SeriesCount, bool IsActive = false)
{
    /// <summary>Two-click "Delete" affordance on the sidebar row
    /// (docs/superpowers/specs/2026-08-28-continuity-editing-design.md). Null on rows built for
    /// pickers (e.g. the Timeline continuity chooser) where delete makes no sense.</summary>
    public TwoStepConfirm? DeleteConfirm { get; init; }
}
