using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// Title search for finding the right remote entry to link a <see cref="Series"/> to, for tracking
/// purposes (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md). Reuses
/// <see cref="MetadataSearchResult"/> from <c>Paperbunkr.Data.Metadata</c> - the shape (ExternalId/
/// Title/Url) is identical to metadata search, and <c>AniListMetadataProvider</c> already implements
/// this exact signature, so it additionally implements this interface directly rather than needing a
/// separate wrapper.
/// </summary>
public interface ITrackerSearchProvider
{
    TrackingService Service { get; }

    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
}
