namespace Paperbunkr.App.Models;

/// <summary>
/// The kind of place a finished job's "Review …" / "View changes →" link navigates to
/// (docs/superpowers/specs/2026-09-03-activity-center-design.md). Resolved centrally by
/// <c>MainViewModel.ResolveActivityLink</c> - add a new destination there, not in the panel.
/// Persisted as the bare enum name in <c>ActivityRun.ResultLinkKind</c>.
/// </summary>
public enum ActivityLinkKind
{
    /// <summary>Open the Library filtered to a transient set of issues (payload = the filter blob).</summary>
    LibrarySavedFilter,

    /// <summary>Open a series detail screen (payload = series id).</summary>
    SeriesDetail,

    /// <summary>Open the update-available overlay / changelog.</summary>
    UpdateChangelog,

    /// <summary>Open the CE migration review overlay.</summary>
    MigrationReview,

    /// <summary>Open Preferences (payload = optional tab key).</summary>
    Preferences,

    /// <summary>Open Smart Lists and the Grouped Review overlay for a specific plugin command (docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-scan-alerts-design.md §4) - payload = <c>"pluginKey|commandKey"</c>.</summary>
    PluginGroupedReview,

    /// <summary>Opens payload (a URL) in the OS default browser, not an in-app navigation like every
    /// other entry here - e.g. the update-available alert's "View release" link to the GitHub
    /// releases page.</summary>
    ExternalUrl,

    /// <summary>Not a navigation destination like every other entry here - relaunches the app
    /// immediately (<c>App.RelaunchAndExit</c>). Raised when a Native-tier plugin install/remove
    /// stays pending until the next restart (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-
    /// tier-design.md §4), so the user isn't left to remember to close and reopen the app themselves.</summary>
    RestartApp,
}
