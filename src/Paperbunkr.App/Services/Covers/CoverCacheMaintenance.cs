namespace Paperbunkr.App.Services.Covers;

/// <summary>
/// Orchestrates the cover-cache responses to a library rebuild
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 2).
/// Shared by the in-process rebuild hooks (CE re-migration, "start fresh") and the startup
/// reconcile that honours a deferred rebuild flag.
/// </summary>
public static class CoverCacheMaintenance
{
    /// <summary>
    /// A library rebuild reassigned entity ids: attic every generated cover (custom covers keep
    /// their own untouched directory), drop the in-memory bitmap caches, and issue a fresh
    /// generation token. Safe to call from any thread - it only touches the filesystem and the
    /// process-wide LRU caches.
    /// </summary>
    public static void PurgeForRebuild()
    {
        CoverCacheAttic.AtticEverything(CoverThumbnailPaths.ThumbnailDirectory, CoverThumbnailPaths.AtticDirectory);
        CoverCacheAttic.AtticEverything(BookCoverThumbnailPaths.ThumbnailDirectory, BookCoverThumbnailPaths.AtticDirectory);
        CoverImageCache.Clear();
        BookCoverImageCache.Clear();
        CoverCacheState.NewGeneration();
    }

    /// <summary>
    /// Defer a rebuild purge to the next startup - for a path (a DB restore that relaunches) that
    /// can't safely attic in-process.
    /// </summary>
    public static void DeferRebuildPurge() => CoverCacheState.MarkRebuildPending();
}
