using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// Local-to-remote-and-back push/pull contract for a tracker service (docs/superpowers/specs/
/// 2026-08-23-tracker-write-back-sync-design.md for Phase A's push half; docs/superpowers/specs/
/// 2026-09-05-two-way-tracker-sync-design.md for <see cref="GetEntryAsync"/>, Phase B). Phase A's own
/// doc comment here said a <c>GetEntryAsync</c> method would be added "only when a Phase B pull/
/// bidirectional spec actually needs it" - this is that spec.
/// </summary>
public interface ITrackerAdapter
{
    TrackingService Service { get; }

    /// <summary>Pushes <paramref name="payload"/> to <paramref name="link"/>'s remote entry. Returns
    /// false on any failure (network, auth, malformed response) - callers surface this as "sync
    /// failed, try again later," never an exception, matching every other external-service call in
    /// this codebase.</summary>
    Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken);

    /// <summary>Reads <paramref name="link"/>'s current remote entry. Null covers every "nothing to
    /// compare against" case uniformly - not connected, not yet tracked on the remote service, or the
    /// call failed - matching this codebase's blanket "unavailable, not exceptional" idiom for read
    /// paths. Callers (<see cref="TrackerSyncResolver"/>) treat null the same as "remote has no
    /// opinion, local wins by default," so collapsing "doesn't exist" and "couldn't check" is safe:
    /// the worst case is a sync pass that pushes local state to a service that was actually just
    /// temporarily unreachable, which self-corrects the next time sync runs successfully.</summary>
    Task<TrackerRemoteEntry?> GetEntryAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken);
}

/// <summary>What one <see cref="ITrackerAdapter.PushEntryAsync"/> call pushes. Chapter progress
/// only, no volume progress, this pass. <see cref="Score"/>/<see cref="FinishDate"/>
/// (docs/superpowers/specs/2026-09-18-per-tracker-score-and-finish-date-design.md) are optional -
/// an adapter for a tracker without one of these fields simply never sets it; not every service
/// supports both (see that design doc's own verified capability table).
///
/// <para><see cref="UpdateScore"/>/<see cref="UpdateFinishDate"/> default <see langword="false"/>
/// deliberately - the shared "Sync with Trackers" button only ever knows Status/Progress, and a
/// tracker whose score lives on a separate endpoint (MangaDex, MangaUpdates) would otherwise issue
/// a real <c>DELETE</c> on every ordinary sync just because <see cref="Score"/> happened to be
/// null, silently wiping a rating the user set independently on that service. Only the per-tracker
/// panel's own explicit field edit sets these <see langword="true"/>, scoped to the one field the
/// user actually touched.</para></summary>
public sealed record TrackerPushPayload(ReadingStatus Status, int? ChapterProgress, decimal? Score = null, DateOnly? FinishDate = null, bool UpdateScore = false, bool UpdateFinishDate = false);

/// <summary>What one <see cref="ITrackerAdapter.GetEntryAsync"/> call reads back. Same
/// <see cref="ReadingStatus"/>/chapter-progress shape as <see cref="TrackerPushPayload"/> -
/// deliberately not the same type, since a future field could apply to only one direction (e.g. a
/// remote-only "last synced at" timestamp would never belong on the push payload).</summary>
public sealed record TrackerRemoteEntry(ReadingStatus Status, int? ChapterProgress, decimal? Score = null, DateOnly? FinishDate = null);
