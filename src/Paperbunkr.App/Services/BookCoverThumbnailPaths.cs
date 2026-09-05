using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Paperbunkr.App.Services;

/// <summary>
/// Cache-file path helper for generated Book cover thumbnails, mirroring
/// <see cref="CoverThumbnailPaths"/> for comics (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §3) - kept as its own cache directory rather than
/// sharing one, since the two caches key on different entity types (Issue.Id vs Book.Id).
///
/// <para>
/// Files are named <c>{bookId}-{fingerprint}.jpg</c> (docs/superpowers/specs/2026-08-27-cover-
/// thumbnail-identity-validation-design.md). Unlike comics - which fold <c>Issue.FileSize</c> into
/// the fingerprint - Books have no persisted size column (and the migration-sensitive branch this
/// shipped on wasn't worth a schema change for it), so the book fingerprint is <b>path only</b>.
/// That still catches the reported failure: a rebuild that reassigns <c>Book.Id</c> puts a
/// different book (different path) on the reused id, so the stem no longer matches.
/// </para>
/// </summary>
public static class BookCoverThumbnailPaths
{
    /// <summary>Mutable so tests can redirect reads/writes to a temp folder - never set this outside a test's own constructor/teardown.</summary>
    public static string ThumbnailDirectory { get; set; } = BuildDefaultDirectory();

    private static string BuildDefaultDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "book-thumbnails");
    }

    /// <summary>Full path of the cache file for a <see cref="CoverFingerprint.Stem"/> value.</summary>
    public static string GetCachePath(string stem)
    {
        Directory.CreateDirectory(ThumbnailDirectory);
        return Path.Combine(ThumbnailDirectory, $"{stem}.jpg");
    }

    /// <summary>Every cache file belonging to <paramref name="bookId"/> (any fingerprint). Returns a
    /// materialized snapshot, empty if the directory is racing with a concurrent delete.</summary>
    public static IReadOnlyList<string> EnumerateForBook(int bookId) => Snapshot($"{bookId}-*.jpg");

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

    /// <summary>Deletes every cached thumbnail belonging to <paramref name="bookId"/>, whatever its
    /// fingerprint - see <see cref="CoverThumbnailPaths.DeleteCachedThumbnail"/> for the id-reuse
    /// bug this mirrors the fix for.</summary>
    public static void DeleteCachedThumbnail(int bookId)
    {
        try
        {
            foreach (string path in EnumerateForBook(bookId).ToList())
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
