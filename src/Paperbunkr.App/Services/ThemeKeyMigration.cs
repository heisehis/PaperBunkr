using System;
using System.Collections.Generic;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>What <see cref="ThemeKeyMigration.Apply"/> changed: whether anything did, and the one Activity Center note to show (null when the user's look did not move).</summary>
public sealed record ThemeKeyMigrationResult(bool Changed, string? Notice);

/// <summary>
/// Moves saved theme keys off the themes retired by the 2026-11-09 roster work (docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md
/// "Migration"): a removed theme becomes its nearest kept theme with the old accent carried over, an OLED variant becomes its family's dark variant
/// with True black on (True black is what selects the OLED variant now), and a bare key of a family that has since gained variants is written out
/// in full. Pure and idempotent: running it again changes nothing.
/// </summary>
public static class ThemeKeyMigration
{
    private sealed record Retired(string OldName, string NewKey, string NewName, string Accent);

    private static readonly Dictionary<string, Retired> RetiredThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cool_technical"] = new("Cool & Technical", "graphite:dark", "Graphite", "#5EA7F0"),
        ["vibrant_pop"] = new("Vibrant & Pop", "default", "Default", "#E85D4E"),
        ["vintage_paperback"] = new("Vintage Paperback", "reading_room:lamplight", "Reading Room · Lamplight", "#8C3B2E"),
        ["overcast"] = new("Overcast", "nordic_frost:light", "Nordic Frost · Light", "#3469A6"),
        ["graphite:light"] = new("Graphite · Light", "windows_11", "Windows 11", "#1A5FC4"),
    };

    private static readonly Dictionary<string, (string DarkKey, string Name)> OledVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["abyss:oled"] = ("abyss:dark", "Abyss"),
        ["tokyo_midnight:oled"] = ("tokyo_midnight:dark", "Tokyo Midnight"),
        ["graphite:oled"] = ("graphite:dark", "Graphite"),
    };

    /// <param name="settings">Edited in place.</param>
    /// <param name="fullKeyOf">The full <c>family:variant</c> key a bare family key now stands for (its first variant) when that family has several
    /// variants, else null. Supplied by the catalog so this class stays pure.</param>
    public static ThemeKeyMigrationResult Apply(AppSettings settings, Func<string, string?> fullKeyOf)
    {
        bool changed = false;
        string? notice = null;

        // The active key first: it alone can carry its accent or switch True black on, and it alone gets a note.
        string active = settings.ActiveThemeKey;
        if (RetiredThemes.TryGetValue(active, out var retired))
        {
            active = retired.NewKey;
            if (string.IsNullOrEmpty(settings.AccentOverrideHex))
            {
                settings.AccentOverrideHex = retired.Accent;
            }

            notice = $"{retired.OldName} was retired. You're now on {retired.NewName}, with {retired.OldName}'s accent kept.";
        }
        else if (OledVariants.TryGetValue(active, out var oled))
        {
            active = oled.DarkKey;
            settings.TrueBlackDark = true;
            notice = $"{oled.Name} · OLED now comes from True black. You're on {oled.Name} with True black on, which looks the same.";
        }

        changed |= Set(settings.ActiveThemeKey, Normalize(active, fullKeyOf), v => settings.ActiveThemeKey = v);
        changed |= Set(settings.LastLightThemeKey, Remembered(settings.LastLightThemeKey, fullKeyOf), v => settings.LastLightThemeKey = v);
        changed |= Set(settings.LastDarkThemeKey, Remembered(settings.LastDarkThemeKey, fullKeyOf), v => settings.LastDarkThemeKey = v);

        return new ThemeKeyMigrationResult(changed, changed ? notice : null);
    }

    /// <summary>A remembered Auto-switch key: retired → its replacement, OLED → dark, bare → full. No accent or True black side effects.</summary>
    private static string? Remembered(string? key, Func<string, string?> fullKeyOf)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        if (RetiredThemes.TryGetValue(key, out var retired))
        {
            key = retired.NewKey;
        }
        else if (OledVariants.TryGetValue(key, out var oled))
        {
            key = oled.DarkKey;
        }

        return Normalize(key, fullKeyOf);
    }

    private static string Normalize(string key, Func<string, string?> fullKeyOf) =>
        key.Contains(':', StringComparison.Ordinal) ? key : fullKeyOf(key) ?? key;

    private static bool Set(string? current, string? value, Action<string> assign)
    {
        if (string.Equals(current, value, StringComparison.Ordinal) || value is null)
        {
            return false;
        }

        assign(value);
        return true;
    }
}
