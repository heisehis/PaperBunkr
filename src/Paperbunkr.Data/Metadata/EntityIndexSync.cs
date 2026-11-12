using System;
using System.Collections.Generic;
using System.Threading;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Keeps the four derived entity indexes (characters, teams, locations, creators) in step with an issue's free-text fields
/// (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-plan.md D1.1). Before this, scans and rescans never touched the
/// indexes and the issue editors synced characters only, so a newly scanned issue was missing from every page built on them.
/// </summary>
public static class EntityIndexSync
{
    /// <summary>Bump when the indexes need a full rebuild on the next run of the rebuild task (<see cref="AppSettings.EntityIndexVersion"/>).</summary>
    public const int CurrentVersion = 1;

    /// <summary>Syncs all four indexes for one issue (set-based, see <see cref="EntityIndexBulk"/>).</summary>
    public static void SyncIssue(PaperbunkrDbContext context, int issueId) => EntityIndexBulk.Sync(context, new[] { issueId });

    /// <summary>Syncs all four indexes for these issues in one set-based pass. Cancellation is checked between batches.</summary>
    public static void SyncIssues(PaperbunkrDbContext context, IEnumerable<int> issueIds, CancellationToken cancellationToken = default)
    {
        var ids = issueIds as IReadOnlyCollection<int> ?? new List<int>(issueIds);
        EntityIndexBulk.Sync(context, ids, cancellationToken);
    }

    /// <summary>Whether the stored version says the indexes still need a full rebuild.</summary>
    public static bool NeedsRebuild(PaperbunkrDbContext context) => context.GetOrCreateAppSettings().EntityIndexVersion < CurrentVersion;

    /// <summary>Rebuilds all four indexes over the whole library (idempotent), then records <see cref="CurrentVersion"/>.</summary>
    public static void RebuildAll(PaperbunkrDbContext context, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Indexing characters, teams, locations and creators…");
        cancellationToken.ThrowIfCancellationRequested();
        EntityIndexBulk.Sync(context, issueIds: null, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        context.GetOrCreateAppSettings().EntityIndexVersion = CurrentVersion;
        context.SaveChanges();
    }
}
