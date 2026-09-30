namespace Paperbunkr.App.Models;

/// <summary>
/// Which view of the open continuity or event is showing: the Continuity screen's Overview | Map | Timeline switch
/// (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md; first a per-item toggle in
/// 2026-08-28-events-continuity-screen-redesign-design.md).
/// </summary>
public enum EventsDetailView
{
    /// <summary>The Overview: hero, attention, runs and events (continuity) or the reading list (event).</summary>
    Primary,

    /// <summary>Era-bucketed layout of this item's issues by comic age.</summary>
    Timeline,

    /// <summary>Swimlane reading-order map (docs/superpowers/specs/2026-09-25-event-map-design.md; continuities since 2026-09-27).</summary>
    Map,
}
