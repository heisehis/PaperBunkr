using System;
using System.IO;

namespace Paperbunkr.App.Services;

/// <summary>
/// Cache-file path helper for downloaded arc-linked reading-list cover art (docs/superpowers/specs/
/// 2026-08-22-cbl-manager-arc-cover-and-synopsis-design.md) - mirrors <see cref="CoverThumbnailPaths"/>'s
/// convention, one JPEG per <c>ReadingList.Id</c> rather than per <c>Issue.Id</c>.
/// </summary>
public static class ArcCoverPaths
{
    /// <summary>Mutable so tests can redirect to a temp folder - never set this outside a test's own constructor/teardown.</summary>
    public static string CacheDirectory { get; set; } = BuildDefaultDirectory();

    private static string BuildDefaultDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "arc-covers");
    }

    public static string GetCachePath(int readingListId)
    {
        Directory.CreateDirectory(CacheDirectory);
        return Path.Combine(CacheDirectory, $"{readingListId}.jpg");
    }
}
