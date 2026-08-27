using System;
using System.IO;

namespace Paperbunkr.App.Plugins;

/// <summary>Path helper for installed plugins (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §2), mirroring <see cref="Services.SkinPaths"/>'s convention: <c>%AppData%\Paperbunkr\plugins\&lt;plugin-key&gt;\</c>, one <c>plugin.xml</c> + script files per plugin.</summary>
public static class PluginPaths
{
    /// <summary>Mutable so tests can redirect discovery at a temp folder instead of the real one - never set this outside a test's own constructor/teardown.</summary>
    public static string RootDirectory { get; set; } = BuildDefaultDirectory();

    private static string BuildDefaultDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "plugins");
    }
}
