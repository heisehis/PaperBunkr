using System;
using System.Globalization;
using System.IO;

namespace Paperbunkr.App.Services.Covers;

/// <summary>
/// Cache-file path helper for user-picked (custom) comic cover art
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md).
///
/// <para>
/// Hand-picked covers live in their <b>own</b> directory - like <see cref="ArcCoverPaths"/> already
/// does for arc art - so the orphan GC and the library-rebuild purge (which only ever touch
/// <see cref="CoverThumbnailPaths.ThumbnailDirectory"/>) can never take them. They are keyed by the
/// bare <c>Issue.Id</c>; the trade-off is that a rebuild reusing an id could show the wrong custom
/// cover, recoverable by re-picking it.
/// </para>
/// </summary>
public static class CustomCoverPaths
{
    /// <summary>Mutable so tests can redirect to a temp folder - never set this outside a test's own setup/teardown.</summary>
    public static string Directory { get; set; } = BuildDefaultDirectory();

    private static string BuildDefaultDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "custom-covers");
    }

    public static string GetCachePath(int issueId)
    {
        System.IO.Directory.CreateDirectory(Directory);
        return Path.Combine(Directory, $"{issueId.ToString(CultureInfo.InvariantCulture)}.jpg");
    }

    public static bool Exists(int issueId)
    {
        try
        {
            return File.Exists(GetCachePath(issueId));
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static void Delete(int issueId)
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
