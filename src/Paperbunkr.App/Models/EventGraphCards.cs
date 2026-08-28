namespace Paperbunkr.App.Models;

/// <summary>One node in the transitive event-relation graph shown on the Story Events detail pane (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-design.md). <see cref="Depth"/> is hop count from the active event; the row indents by it.</summary>
public sealed class EventFamilyNodeCard
{
    public required int EventId { get; init; }
    public required string Name { get; init; }
    public required int Depth { get; init; }
    public bool IsRoot => Depth == 0;

    /// <summary>Left indent in device-independent pixels, derived from <see cref="Depth"/> - lets the DataTemplate stay converter-free.</summary>
    public double Indent => Depth * 18.0;
}

/// <summary>One "these two events look connected" candidate for the Connect Event flow (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-design.md).</summary>
public sealed class EventConnectionSuggestionCard
{
    public required int CandidateEventId { get; init; }
    public required string Name { get; init; }
    public required string Reason { get; init; }
}

/// <summary>One issue the user has dismissed from an event's suggestion queue, restorable (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md).</summary>
public sealed class DismissedSuggestionCard
{
    public required int IssueId { get; init; }
    public required string Label { get; init; }
}

/// <summary>One overlapping continuity in the cross-continuity compare picker (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-browse-design.md).</summary>
public sealed class ContinuityOverlapCard
{
    public required int ContinuityId { get; init; }
    public required string Name { get; init; }
    public required int SharedSeriesCount { get; init; }
    public string SharedSeriesLabel => SharedSeriesCount == 1 ? "1 shared series" : $"{SharedSeriesCount} shared series";
}
