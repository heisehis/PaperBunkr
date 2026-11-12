using System.IO;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// Where the user's own collection icons live: <c>%APPDATA%\Paperbunkr\collection-icons</c> (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md).
/// Anything the user collects goes here; the app ships nothing into it. Mirrors <see cref="PublisherIconFolder"/>.
/// </summary>
public static class CollectionIconFolder
{
    public static string Path => AppDataPaths.Combine("collection-icons");

    /// <summary>Creates the folder if it does not exist and returns its path.</summary>
    public static string Ensure()
    {
        Directory.CreateDirectory(Path);
        return Path;
    }
}
