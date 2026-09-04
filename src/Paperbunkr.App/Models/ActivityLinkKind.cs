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
}
