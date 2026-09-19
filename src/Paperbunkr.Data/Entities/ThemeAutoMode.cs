namespace Paperbunkr.Data.Entities;

/// <summary>
/// Whether the active theme auto-switches between the user's last-picked Light/Dark pair, backing
/// <see cref="AppSettings.ThemeAutoMode"/> (docs/superpowers/specs/2026-09-16-theme-system-design.md
/// § Extended scope). Not a per-theme <c>mode</c> value - a selection layer above the theme picker.
/// </summary>
public enum ThemeAutoMode
{
    /// <summary>No automatic switching - whatever theme the user picked stays active. Default.</summary>
    Off,

    /// <summary>Follows the OS light/dark preference, read once per check (startup, and on a live
    /// <c>PlatformSettings.ColorValuesChanged</c> notification) - never a per-frame poll.</summary>
    FollowSystem,

    /// <summary>Switches at a configured local hour (<see cref="AppSettings.ThemeScheduledDarkHour"/>),
    /// via a 300ms crossfade rather than an instant cut.</summary>
    Scheduled
}
