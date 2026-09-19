namespace Paperbunkr.Data.Entities;

/// <summary>
/// What "Update progress when marked as read" does when the user manually marks issues read
/// (docs/superpowers/specs/2026-09-18-tracker-behavior-settings-design.md §3.3) - Komikku's
/// <c>AutoTrackState</c>. App-wide setting on <see cref="AppSettings"/>.
/// </summary>
public enum TrackerAutoUpdateMode
{
    /// <summary>Push to linked trackers right away (the default).</summary>
    Always,

    /// <summary>Show an "Update trackers?" toast with an action; push only if accepted.</summary>
    Ask,

    /// <summary>Never push on a manual mark-as-read.</summary>
    Never,
}
