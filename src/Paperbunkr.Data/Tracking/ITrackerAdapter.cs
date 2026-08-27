using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// One-way local-to-remote push contract for a tracker service (docs/superpowers/specs/2026-08-23-
/// tracker-write-back-sync-design.md) - Phase A scope only. Deliberately excludes a
/// <c>GetEntryAsync</c> method (present in the original single-service sketch's own draft interface)
/// since nothing in Phase A ever reads a remote entry; add it back only when a Phase B pull/
/// bidirectional spec actually needs it.
/// </summary>
public interface ITrackerAdapter
{
    TrackingService Service { get; }

    /// <summary>Pushes <paramref name="payload"/> to <paramref name="link"/>'s remote entry. Returns
    /// false on any failure (network, auth, malformed response) - callers surface this as "sync
    /// failed, try again later," never an exception, matching every other external-service call in
    /// this codebase.</summary>
    Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken);
}

/// <summary>What one <see cref="ITrackerAdapter.PushEntryAsync"/> call pushes. Chapter progress only,
/// no volume progress, this pass.</summary>
public sealed record TrackerPushPayload(ReadingStatus Status, int? ChapterProgress);
