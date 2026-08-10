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
}
