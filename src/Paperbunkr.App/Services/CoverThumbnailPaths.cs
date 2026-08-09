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
}
