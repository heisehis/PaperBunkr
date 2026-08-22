using System;
using System.IO;

namespace Paperbunkr.App.Services;

/// <summary>
/// Cache-file path helper for generated cover thumbnails (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md
/// §1), mirroring <see cref="Paperbunkr.Data.PaperbunkrDbContext.GetDefaultDatabasePath"/>'s
/// %AppData%\Paperbunkr convention. One JPEG per Issue - a Series' cover is just whichever issue
/// its CoverIssueId (or first issue) points to, so there's no separate series-level file.
/// </summary>
public static class CoverThumbnailPaths
{
    /// <summary>
    /// Mutable so tests can redirect reads/writes to a temp folder instead of the real cache -
    /// never set this outside a test's own constructor/teardown.
    /// </summary>
    public static string ThumbnailDirectory { get; set; } = BuildDefaultDirectory();

    private static string BuildDefaultDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "thumbnails");
    }

    public static string GetCachePath(int issueId)
    {
        Directory.CreateDirectory(ThumbnailDirectory);
        return Path.Combine(ThumbnailDirectory, $"{issueId}.jpg");
    }

    /// <summary>
    /// Deletes issueId's cached thumbnail file, if any. Real bug found + fixed 2026-08-19: this
    /// cache is keyed purely by the auto-increment Issue.Id, which isn't stable across a library
    /// reset/re-migration - a fresh migration's Issue #411 can land on the same numeric id an
    /// earlier, since-deleted Issue #411 had, and <see cref="GetCachePath"/>'s "skip if a file
    /// already exists" check in <c>CoverThumbnailService</c> then silently serves that orphaned,
    /// unrelated cover forever. Called from every real Issue-deletion call site
    /// (<c>IssuePropertiesScreenViewModel.Cancel</c>, <c>NeedsReviewViewModel.RemoveMissingFile</c>/
    /// <c>MergeSeriesInto</c>) so an orphan can no longer accumulate going forward - swallows
    /// <see cref="IOException"/> the same way this codebase's other cache-file cleanup already does
    /// (a locked/already-gone file isn't worth failing the caller's own deletion over).
    /// </summary>
    public static void DeleteCachedThumbnail(int issueId)
    {
        try
        {
            string path = GetCachePath(issueId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
