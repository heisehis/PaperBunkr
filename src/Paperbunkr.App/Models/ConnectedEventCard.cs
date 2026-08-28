namespace Paperbunkr.App.Models;

/// <summary>
/// One event connected to the active event, shown as a card in the Story Events screen's "Connected
/// Events" section (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-
/// design.md). <see cref="RelationLabel"/> is already resolved for the side being viewed;
/// <see cref="EventRelationId"/> backs the unlink action and <see cref="OtherEventId"/> backs the
/// click-to-switch-active-event action.
/// </summary>
public sealed class ConnectedEventCard
{
    public required int EventRelationId { get; init; }
    public required int OtherEventId { get; init; }
    public required string Name { get; init; }
    public required string RelationLabel { get; init; }
}
