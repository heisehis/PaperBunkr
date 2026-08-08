using System;
using System.IO;

namespace Paperbunkr.App.Services;

/// <summary>
/// Path helper for installed/extracted skins (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §3), mirroring the <see cref="CoverThumbnailPaths"/> convention: installed <c>.crpck</c> files at
/// <c>%AppData%\Paperbunkr\skins\</c>, extracted once to <c>%AppData%\Paperbunkr\skins-extracted\{key}\</c>
/// so every consumer does plain file-path lookups, no <c>ZipArchive</c> API on any hot path.
/// </summary>
public static class SkinPaths
{
    /// <summary>
    /// Mutable so tests can redirect reads/writes to a temp folder instead of the real cache -
    /// never set these outside a test's own constructor/teardown.
    /// </summary>
    public static string InstalledDirectory { get; set; } = BuildDefaultDirectory("skins");

    public static string ExtractedDirectory { get; set; } = BuildDefaultDirectory("skins-extracted");

    private static string BuildDefaultDirectory(string leaf)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", leaf);
    }
}
