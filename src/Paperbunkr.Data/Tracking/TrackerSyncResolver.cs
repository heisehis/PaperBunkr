using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// Two-way tracker sync's conflict rule (docs/superpowers/specs/2026-09-05-two-way-tracker-sync-
/// design.md): "keep whichever side is further along" - Komikku's own one-way-plus rule
/// (docs/tracker-manga-ui-research.md §1.5, "the app keeps the higher last-read chapter"), extended
/// to also cover <see cref="ReadingStatus"/> rather than just the numeric chapter count, since this
/// codebase tracks a richer status than Mihon/Komikku's trackers do. Chapter progress is the primary
/// signal; status only breaks a tie (both sides at the same progress, e.g. a series just linked with
/// nothing read on either end yet) - deliberately not an independent status-merge policy, so there's
/// one "further along" concept, not two that could disagree with each other.
/// </summary>
public static class TrackerSyncResolver
{
    /// <summary>True when the remote entry is further along than local and should be pulled;
    /// false means local is further along (or tied) and should be pushed instead. A pulled remote
    /// state is user-approved as authoritative (2026-09-05) - no confirmation prompt, matching every
    /// other tracker action in this codebase collapsing to a silent toast/status line.</summary>
    public static bool RemoteWins(int? localProgress, ReadingStatus localStatus, TrackerRemoteEntry remote)
    {
        int local = localProgress ?? -1;
        int remoteProgress = remote.ChapterProgress ?? -1;

        if (remoteProgress != local)
        {
            return remoteProgress > local;
        }

        return StatusRank(remote.Status) > StatusRank(localStatus);
    }

    /// <summary>Applies a pulled remote entry to <paramref name="series"/>: adopts the remote
    /// <see cref="ReadingStatus"/>, and marks every not-yet-read <see cref="Issue"/> whose parsed
    /// number is at or below the remote chapter progress as read via
    /// <see cref="IssueReadStateResolver.MarkAsRead"/> - the user's own choice (2026-09-05) over only
    /// displaying the remote progress, made with the caveat that <c>Issue.Number</c> isn't guaranteed
    /// to line up with a tracker's own chapter numbering (TPB folding, variant issues). Silently
    /// no-ops on any issue whose <see cref="Issue.PageCount"/> isn't known yet - same "no real 'last
    /// page' to mark" safety <see cref="IssueReadStateResolver.MarkAsRead"/> already has, inherited
    /// rather than duplicated here. Returns the issues actually marked read this call (an issue whose
    /// <c>PageCount</c> is unknown, or whose number already qualified but was already read, isn't
    /// included) so a caller holding its own display-tile cache (<c>DetailTabsViewModel.Issues</c>)
    /// knows exactly which tiles need swapping, without diffing the whole series itself.</summary>
    public static IReadOnlyList<Issue> ApplyRemote(Series series, TrackerRemoteEntry remote)
    {
        series.ReadingStatus = remote.Status;

        if (remote.ChapterProgress is not int progress)
        {
            return System.Array.Empty<Issue>();
        }

        var newlyRead = new List<Issue>();
        foreach (var issue in series.Issues.Where(i => !i.HasBeenRead()))
        {
            if (issue.NumberSortKey() is not float number || number > progress)
            {
                continue;
            }

            IssueReadStateResolver.MarkAsRead(issue);
            if (issue.HasBeenRead())
            {
                newlyRead.Add(issue);
            }
        }

        return newlyRead;
    }

    /// <summary>Simple three-tier progression, used only as a tie-break when chapter progress is
    /// equal on both sides. <see cref="ReadingStatus.Unknown"/> ranks lowest so a remote service that
    /// returned an unrecognized/unmapped status never outranks a genuinely-known local one.</summary>
    private static int StatusRank(ReadingStatus status) => status switch
    {
        ReadingStatus.Completed => 3,
        ReadingStatus.Reading => 2,
        ReadingStatus.ReReading => 2,
        ReadingStatus.Planned => 1,
        ReadingStatus.Paused => 1,
        ReadingStatus.Dropped => 1,
        _ => 0,
    };
}
