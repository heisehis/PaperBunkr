using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Paperbunkr.App.Services;

/// <summary>
/// Cache-file path helper for generated Book cover thumbnails, mirroring
/// <see cref="CoverThumbnailPaths"/> for comics - its own cache directory since the two key on
/// different entity types (<c>Issue.Id</c> vs <c>Book.Id</c>). One JPEG per <c>Book.Id</c>, named
/// <c>{bookId}.jpg</c> (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-
/// design.md - the previous <c>{id}-{hash}.jpg</c> fingerprint scheme is retired).
/// </summary>
public static class BookCoverThumbnailPaths
{
    /// <summary>Mutable so tests can redirect reads/writes to a temp folder - never set this outside a test's own constructor/teardown.</summary>
    public static string ThumbnailDirectory { get; set; } = BuildDefaultDirectory();

    /// <summary>Soft-delete holding area for covers the orphan GC removed - pruned by age + size, restorable by id.</summary>
    public static string AtticDirectory => Path.Combine(ThumbnailDirectory, ".attic");

    private static string BuildDefaultDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "book-thumbnails");
    }

    /// <summary>Full path of the cache file for a <see cref="CoverFingerprint.Stem"/> value (the bare id).</summary>
    public static string GetCachePath(string stem)
    {
        Directory.CreateDirectory(ThumbnailDirectory);
        return Path.Combine(ThumbnailDirectory, $"{stem}.jpg");
    }

    /// <summary>Full path of the current-scheme cache file for <paramref name="bookId"/>.</summary>
    public static string GetCachePath(int bookId) =>
        GetCachePath(bookId.ToString(CultureInfo.InvariantCulture));

    /// <summary>Every cache file belonging to <paramref name="bookId"/> - the current
    /// <c>{id}.jpg</c> plus any legacy <c>{id}-*.jpg</c>. Materialized snapshot, empty if the
    /// directory is racing a concurrent delete.</summary>
    public static IReadOnlyList<string> EnumerateForBook(int bookId)
    {
        string id = bookId.ToString(CultureInfo.InvariantCulture);
        return Snapshot($"{id}.jpg").Concat(Snapshot($"{id}-*.jpg")).Distinct().ToList();
    }

    /// <summary>Every current cache file in the directory - for the orphan GC.</summary>
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

    /// <summary>Deletes every cached thumbnail belonging to <paramref name="bookId"/> (current + legacy).</summary>
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
