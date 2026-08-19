using System;
using System.IO;

namespace Paperbunkr.App.Services;

/// <summary>
/// Cache-file path helper for generated Book cover thumbnails, mirroring
/// <see cref="CoverThumbnailPaths"/> for comics (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §3) - kept as its own cache directory rather than
/// sharing one, since the two caches key on different entity types (Issue.Id vs Book.Id, which
/// aren't unique against each other).
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

    public static string GetCachePath(int bookId)
    {
        Directory.CreateDirectory(ThumbnailDirectory);
        return Path.Combine(ThumbnailDirectory, $"{bookId}.jpg");
    }

    /// <summary>Deletes bookId's cached thumbnail file, if any - see <see cref="CoverThumbnailPaths.DeleteCachedThumbnail"/>
    /// for the real bug this mirrors the fix for (same Id-reuse-across-a-reset risk applies here too).</summary>
    public static void DeleteCachedThumbnail(int bookId)
    {
        try
        {
            string path = GetCachePath(bookId);
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
