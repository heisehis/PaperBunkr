namespace Paperbunkr.App.Models;

/// <summary>
/// Which view of the selected event or continuity is showing (docs/superpowers/specs/
/// 2026-08-28-events-continuity-screen-redesign-design.md). Replaces the old top-level
/// <see cref="EventsScreenMode"/> Timeline mode - Timeline is now a per-item toggle.
/// </summary>
public enum EventsDetailView
{
    /// <summary>Member list (event) or member-series grid (continuity).</summary>
    Primary,

    /// <summary>Era-bucketed layout of this item's issues by comic age.</summary>
    Timeline,
}
