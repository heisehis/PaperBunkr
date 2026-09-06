using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Paperbunkr.App.Services;

/// <summary>
/// Cache-file path helper for generated comic cover thumbnails (docs/superpowers/specs/2026-08-06-
/// cover-thumbnails-design.md §1), mirroring
/// <see cref="Paperbunkr.Data.PaperbunkrDbContext.GetDefaultDatabasePath"/>'s %AppData%\Paperbunkr
/// convention. One JPEG per <c>Issue.Id</c>, named <c>{issueId}.jpg</c>
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md - the previous
/// <c>{id}-{hash}.jpg</c> fingerprint scheme is retired; see <see cref="CoverFingerprint"/>).
///
/// <para>
/// A cover that no longer matches any issue is <b>moved to <see cref="AtticDirectory"/></b> by the
/// orphan GC, never deleted outright - a routine file-path change must not destroy derived art.
/// </para>
/// </summary>
public static class CoverThumbnailPaths
{
    /// <summary>Mutable so tests can redirect reads/writes to a temp folder - never set this outside a test's own constructor/teardown.</summary>
    public static string ThumbnailDirectory { get; set; } = BuildDefaultDirectory();

    /// <summary>Soft-delete holding area for covers the orphan GC removed - pruned by age + size, restorable by id.</summary>
    public static string AtticDirectory => Path.Combine(ThumbnailDirectory, ".attic");

    private static string BuildDefaultDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "thumbnails");
    }

    /// <summary>Full path of the cache file for a <see cref="CoverFingerprint.Stem"/> value (the bare id).</summary>
    public static string GetCachePath(string stem)
    {
        Directory.CreateDirectory(ThumbnailDirectory);
        return Path.Combine(ThumbnailDirectory, $"{stem}.jpg");
    }

    /// <summary>Full path of the current-scheme cache file for <paramref name="issueId"/>.</summary>
    public static string GetCachePath(int issueId) =>
        GetCachePath(issueId.ToString(CultureInfo.InvariantCulture));

    /// <summary>Every cache file belonging to <paramref name="issueId"/> - the current
    /// <c>{id}.jpg</c> plus any legacy <c>{id}-*.jpg</c> left from the fingerprint scheme. Returns a
    /// materialized snapshot (empty if the directory is racing a concurrent delete from another app
    /// instance).</summary>
    public static IReadOnlyList<string> EnumerateForIssue(int issueId)
    {
        string id = issueId.ToString(CultureInfo.InvariantCulture);
        return Snapshot($"{id}.jpg").Concat(Snapshot($"{id}-*.jpg")).Distinct().ToList();
    }

    /// <summary>Every current cache file in the directory - for the orphan GC. Excludes <see cref="AtticDirectory"/> (a subfolder, so a non-recursive enumeration already skips it).</summary>
    public static IReadOnlyList<string> EnumerateAll() => Snapshot("*.jpg");

    /// <summary>Every file currently sitting in the attic.</summary>
    public static IReadOnlyList<string> EnumerateAttic()
    {
        try
        {
            Directory.CreateDirectory(AtticDirectory);
            return Directory.GetFiles(AtticDirectory, "*.jpg");
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
    /// Deletes every cached thumbnail belonging to <paramref name="issueId"/> (current + legacy).
    /// Called from every real Issue-deletion call site so an orphan can't accumulate through those
    /// paths. Swallows <see cref="IOException"/> the same way this codebase's other cache-file
    /// cleanup does.
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
