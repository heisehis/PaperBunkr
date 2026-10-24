using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Splash;

/// <summary>
/// Reads and writes <c>splash-theme.json</c> next to the database, like <c>insights.json</c> and <c>keymap.json</c>. A missing, unreadable or
/// malformed file reads as null and is never fatal; saving is best-effort. Construct with no path for an in-memory instance (tests).
/// </summary>
public sealed class SplashThemeStore
{
    private const int Version = 1;
    private readonly string? _path;
    private SplashThemeSnapshot? _memory;

    public SplashThemeStore(string? path = null) => _path = path;

    /// <summary>The default location: the database's folder, so a database-path override moves it too.</summary>
    public static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(Paperbunkr.Data.PaperbunkrDbContext.GetDefaultDatabasePath()) ?? ".", "splash-theme.json");

    public SplashThemeSnapshot? Load()
    {
        if (_path is null)
        {
            return _memory;
        }

        try
        {
            return File.Exists(_path) ? Parse(File.ReadAllText(_path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(SplashThemeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_path is null)
        {
            _memory = snapshot;
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            string temp = _path + ".tmp";
            File.WriteAllText(temp, Serialize(snapshot));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticsService.LogMilestone($"Splash theme snapshot not saved ({ex.GetType().Name}).");
        }
    }

    internal static string Serialize(SplashThemeSnapshot s)
    {
        var root = new JsonObject
        {
            ["version"] = Version,
            ["autoMode"] = s.AutoMode.ToString(),
            ["darkHour"] = s.DarkHour,
            ["lightHour"] = s.LightHour,
            ["current"] = Write(s.Current),
            ["light"] = s.Light is null ? null : Write(s.Light),
            ["dark"] = s.Dark is null ? null : Write(s.Dark),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Null when the text is not a usable snapshot (bad JSON, unknown version, no usable current palette).</summary>
    internal static SplashThemeSnapshot? Parse(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["version"]?.GetValue<int>() != Version)
            {
                return null;
            }

            var current = Read(root["current"]);
            if (current is null)
            {
                return null;
            }

            var mode = Enum.TryParse<ThemeAutoMode>(root["autoMode"]?.GetValue<string>(), out var m) ? m : ThemeAutoMode.Off;
            int dark = root["darkHour"]?.GetValue<int>() ?? 20;
            int light = root["lightHour"]?.GetValue<int>() ?? 7;
            return new SplashThemeSnapshot(mode, dark, light, current, Read(root["light"]), Read(root["dark"]));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static JsonObject Write(SplashPalette p) => new()
    {
        ["isLight"] = p.IsLight,
        ["surface"] = Hex(p.Surface),
        ["text"] = Hex(p.Text),
        ["textMuted"] = Hex(p.TextMuted),
        ["textFaint"] = Hex(p.TextFaint),
        ["border"] = Hex(p.Border),
        ["accent"] = Hex(p.Accent),
        ["accentText"] = Hex(p.AccentText),
        ["accentSoft"] = Hex(p.AccentSoft),
        ["cornerRadius"] = p.CornerRadius,
    };

    private static SplashPalette? Read(JsonNode? node)
    {
        if (node is not JsonObject o)
        {
            return null;
        }

        Color C(string key) => Color.Parse(o[key]?.GetValue<string>() ?? throw new FormatException(key));
        return new SplashPalette(
            o["isLight"]?.GetValue<bool>() ?? false,
            C("surface"), C("text"), C("textMuted"), C("textFaint"), C("border"), C("accent"), C("accentText"), C("accentSoft"),
            o["cornerRadius"]?.GetValue<double>() ?? 14);
    }

    private static string Hex(Color c) => string.Create(CultureInfo.InvariantCulture, $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}");
}
