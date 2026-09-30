using System.Globalization;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Models;

/// <summary>
/// Sidebar row for one <c>Continuity</c> (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-browse-design.md; the
/// publisher logo and issue count from docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md) - mirrors
/// <see cref="StoryEventSummary"/>'s shape.
/// </summary>
public sealed record ContinuitySummary(int Id, string Name, string? Publisher, int SeriesCount, bool IsActive = false)
{
    /// <summary>Two-click "Delete" affordance on the sidebar row
    /// (docs/superpowers/specs/2026-08-28-continuity-editing-design.md). Null on rows built for
    /// pickers where delete makes no sense.</summary>
    public TwoStepConfirm? DeleteConfirm { get; init; }

    /// <summary>Distinct issues across the member series (placeholders excluded).</summary>
    public int IssueCount { get; init; }

    public string IssueCountLabel => IssueCount.ToString("N0", CultureInfo.CurrentCulture);

    public bool HasPublisher => !string.IsNullOrWhiteSpace(Publisher);
}
