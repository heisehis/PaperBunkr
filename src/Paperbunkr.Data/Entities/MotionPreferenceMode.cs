namespace Paperbunkr.Data.Entities;

/// <summary>
/// The app-wide Motion preference (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md §4.1).
/// System follows the platform animation setting (off → Reduced, on → Full); Reduced keeps short fades and drops all travel;
/// Off applies every final state immediately. Stored as its name.
/// </summary>
public enum MotionPreferenceMode
{
    System,
    Full,
    Reduced,
    Off,
}
