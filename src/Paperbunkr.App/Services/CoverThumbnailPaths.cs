using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Paperbunkr.App.Services;

/// <summary>
/// Cache-file path helper for generated cover thumbnails (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md
/// §1), mirroring <see cref="Paperbunkr.Data.PaperbunkrDbContext.GetDefaultDatabasePath"/>'s
/// %AppData%\Paperbunkr convention. One JPEG per Issue - a Series' cover is just whichever issue
/// its CoverIssueId (or first issue) points to, so there's no separate series-level file.
///
/// <para>
/// Files are named <c>{issueId}-{fingerprint}.jpg</c> (docs/superpowers/specs/2026-08-27-cover-
/// thumbnail-identity-validation-design.md) - see <see cref="CoverFingerprint"/>. The bare
/// <c>{issueId}.jpg</c> form this used to write is no longer produced or read; any left over from
/// before the change is swept by <c>CoverThumbnailService.GenerateAllAsync</c>'s orphan GC.
/// </para>
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

    /// <summary>Full path of the cache file for a <see cref="CoverFingerprint.Stem"/> value.</summary>
    public static string GetCachePath(string stem)
    {
        Directory.CreateDirectory(ThumbnailDirectory);
        return Path.Combine(ThumbnailDirectory, $"{stem}.jpg");
    }

    /// <summary>Every cache file belonging to <paramref name="issueId"/> (any fingerprint) - i.e.
    /// <c>{issueId}-*.jpg</c>. Used to sweep a stale sibling when a fresh one is generated. Returns a
    /// materialized snapshot (and an empty one if the directory is racing with a concurrent delete
    /// from another app instance - several run against the same cache).</summary>
    public static IReadOnlyList<string> EnumerateForIssue(int issueId) => Snapshot($"{issueId}-*.jpg");

    /// <summary>Every cache file in the directory - for <c>GenerateAllAsync</c>'s orphan GC.</summary>
    public static IReadOnlyList<string> EnumerateAll() => Snapshot("*.jpg");

    private static IReadOnlyList<string> Snapshot(string pattern)
    {
        try
        {
            Directory.CreateDirectory(ThumbnailDirectory);
            return Directory.GetFiles(ThumbnailDirectory, pattern);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Deletes every cached thumbnail belonging to <paramref name="issueId"/>, whatever its
    /// fingerprint. Real bug found + fixed 2026-08-19 (kept relevant by the fingerprint rework):
    /// this cache is keyed by the auto-increment Issue.Id, which isn't stable across a library
    /// reset/re-migration. Called from every real Issue-deletion call site
    /// (<c>IssuePropertiesScreenViewModel.Cancel</c>, <c>NeedsReviewViewModel.RemoveMissingFile</c>/
    /// <c>MergeSeriesInto</c>, <c>LibraryDeletionHelper</c>) so an orphan can no longer accumulate
    /// through those paths - the fingerprint in the filename plus <c>GenerateAllAsync</c>'s orphan
    /// GC covers the rebuild paths that don't go through per-issue deletion. Swallows
    /// <see cref="IOException"/> the same way this codebase's other cache-file cleanup already does.
    /// </summary>
    public static void DeleteCachedThumbnail(int issueId)
    {
        try
        {
            foreach (string path in EnumerateForIssue(issueId).ToList())
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
