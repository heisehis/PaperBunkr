using System.IO;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// Where the user's own publisher logos live: <c>%APPDATA%\Paperbunkr\publisher-icons</c>
/// (docs/superpowers/specs/2026-10-04-publisher-icons-user-folder-and-gaps-design.md). Kept apart from <see cref="MarkResolver"/> so asking for the path
/// does not construct the resolver (which reads bundled assets and so needs a running Avalonia app).
/// </summary>
public static class PublisherIconFolder
{
    public static string Path => AppDataPaths.Combine("publisher-icons");

    /// <summary>Creates the folder if it does not exist and returns its path.</summary>
    public static string Ensure()
    {
        Directory.CreateDirectory(Path);
        return Path;
    }
}
