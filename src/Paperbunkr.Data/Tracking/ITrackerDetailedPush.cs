using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// The detailed-error push every real adapter implements (real server error text instead of a bare
/// <see langword="false"/>) - the pattern that found the MangaBaka 404, the MangaDex User-Agent and the
/// AniList "progress must be an integer" bugs. Separate from <see cref="ITrackerAdapter"/> so simple
/// fakes needn't implement it; <see cref="TrackerAdapterFactory.PushDetailedAsync"/> uses it when present.
/// </summary>
public interface ITrackerDetailedPush
{
    Task<(bool Success, string? ErrorDetail)> PushEntryDetailedAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken);
}
