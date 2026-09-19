using System.Collections.Generic;
using System.Threading.Tasks;

namespace Paperbunkr.App.Services;

/// <summary>
/// The automatic (non-button) tracker push/pull paths - docs/superpowers/specs/2026-09-18-tracker-
/// behavior-settings-design.md. Screen ViewModels call these at their existing hook points; every
/// method reads the relevant <c>AppSettings</c> toggle fresh, so a setting takes effect without a
/// restart, and every method is safe to call when nothing is linked or connected (it just returns).
/// Returned tasks complete when the work does - production callers discard them, tests await them.
/// </summary>
public interface ITrackerAutoSyncService
{
    /// <summary>The comic reader crossed its "finished" line for an issue in <paramref name="seriesId"/>
    /// (setting: update progress after reading).</summary>
    Task OnIssueFinishedInReaderAsync(int seriesId);

    /// <summary>The user manually marked issues read (never unread, never a tracker pull's own
    /// <c>ApplyRemote</c>) for these series (setting: update progress when marked as read -
    /// Always pushes, Ask shows an actionable toast, Never does nothing).</summary>
    Task OnIssuesMarkedReadAsync(IReadOnlyCollection<int> seriesIds);

    /// <summary>A linked series' detail screen opened (setting: auto sync from trackers). Applies any
    /// remote-ahead progress locally and returns the ids of issues newly marked read so the caller can
    /// refresh its tiles. Never pushes.</summary>
    Task<TrackerPullResult> PullSeriesAsync(int seriesId);
}

/// <summary>Outcome of an automatic pull - <see cref="NewlyReadIssueIds"/> is empty when nothing changed.</summary>
public sealed record TrackerPullResult(IReadOnlyList<int> NewlyReadIssueIds)
{
    public static readonly TrackerPullResult None = new(System.Array.Empty<int>());
}

/// <summary>Null-object default for screen ViewModels constructed without the real service (tests,
/// design-time) - no-ops, never null.</summary>
public sealed class NoOpTrackerAutoSyncService : ITrackerAutoSyncService
{
    public static readonly NoOpTrackerAutoSyncService Instance = new();

    public Task OnIssueFinishedInReaderAsync(int seriesId) => Task.CompletedTask;

    public Task OnIssuesMarkedReadAsync(IReadOnlyCollection<int> seriesIds) => Task.CompletedTask;

    public Task<TrackerPullResult> PullSeriesAsync(int seriesId) => Task.FromResult(TrackerPullResult.None);
}
