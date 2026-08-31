using System;
using System.IO;

namespace Paperbunkr.App.Plugins;

/// <summary>Path helper for installed plugins (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §2), mirroring <see cref="Services.SkinPaths"/>'s convention: <c>%AppData%\Paperbunkr\plugins\&lt;plugin-key&gt;\</c>, one <c>plugin.xml</c> + script files per plugin.</summary>
public static class PluginPaths
{
    /// <summary>Mutable so tests can redirect discovery at a temp folder instead of the real one - never set this outside a test's own constructor/teardown.</summary>
    public static string RootDirectory { get; set; } = BuildDefaultDirectory();

    /// <summary>
    /// <c>PluginPackageService</c>'s staging folder for <c>PackageManager</c>'s pending-install/
    /// pending-remove bookkeeping - kept as a sibling of <see cref="RootDirectory"/> rather than a
    /// subfolder inside it, so <c>PluginEngine.Discover</c>'s <c>SearchOption.AllDirectories</c> walk
    /// of <see cref="RootDirectory"/> never wanders into a half-installed package.
    /// </summary>
    public static string StagingDirectory { get; set; } = BuildDefaultStagingDirectory();

    private static string BuildDefaultDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "plugins");
    }

    private static string BuildDefaultStagingDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "plugin-staging");
    }
}
