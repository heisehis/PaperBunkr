namespace Paperbunkr.App.Models;

/// <summary>Which direction a drill-down navigation (docs/superpowers/specs/2026-09-04-navigation-
/// transition-system-design.md) is moving, driving both the visual push/pop transition and whether
/// <see cref="Paperbunkr.App.Services.NavigationTransitionCoordinator"/> attempts a shared-element
/// cover flight forward or backward. <see cref="None"/> is the reduced-motion / design-time value -
/// no transition plays at all, same as the lateral rail system's existing reduced-motion gap being
/// avoided deliberately here (see the design doc's motion-tokens section).</summary>
public enum DrillTransitionKind
{
    None,
    Push,
    Pop,
}
