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

    /// <summary>Reads <paramref name="link"/>'s current remote entry and distinguishes a confirmed
    /// absent entry from a transient or permanent failure. Automatic pushes must not proceed when
    /// the lookup failed transiently because remote progress could not be checked.</summary>
    Task<TrackerRemoteEntry?> GetEntryAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken);

    /// <summary>Classified lookup used by unattended retry execution. The default is deliberately
    /// permanent for legacy adapters: a nullable result cannot safely distinguish absence from an
    /// unavailable lookup. Real adapters should override this contract as they gain typed handling.</summary>
    async Task<TrackerOperationResult<TrackerRemoteEntry>> GetEntryOutcomeAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken)
    {
        var entry = await GetEntryAsync(context, link, cancellationToken).ConfigureAwait(false);
        return entry is null
            ? TrackerOperationResult<TrackerRemoteEntry>.Permanent("Could not verify remote progress.")
            : TrackerOperationResult<TrackerRemoteEntry>.Success(entry);
    }
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
