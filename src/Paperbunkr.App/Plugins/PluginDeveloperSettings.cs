using System;
using System.IO;
using System.Text.Json;
using Paperbunkr.Data;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// The plugin developer-mode switch (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §A1), kept in
/// <c>plugin-developer.json</c> beside the database rather than in <c>AppSettings</c>: it is a per-user tooling preference, not
/// library data, and a file needs no migration. Missing or unreadable means off. Never throws.
/// </summary>
public static class PluginDeveloperSettings
{
    /// <summary>Test seam - where the file lives.</summary>
    internal static string FilePath { get; set; } = AppDataPaths.Combine("plugin-developer.json");

    public static bool LoadDeveloperMode()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            return document.RootElement.TryGetProperty("developerMode", out JsonElement value) && value.ValueKind == JsonValueKind.True;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void SaveDeveloperMode(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new { developerMode = enabled }));
        }
        catch (Exception)
        {
            // Not worth failing a toggle over; the switch simply won't survive a restart.
        }
    }
}
