namespace Paperbunkr.App.Models;

/// <summary>
/// What kind of navigation produced the current screen, so the route transition can show it
/// (docs/superpowers/specs/2026-10-20-motion-system-v2-navigation-design.md §3.2): a rail switch settles up, a drill-in grows from
/// slightly smaller, Back reverses that, the readers just fade, and anything unknown fades.
/// </summary>
public enum RouteIntent
{
    Neutral,
    Lateral,
    DrillIn,
    Back,
    Reader,
}
