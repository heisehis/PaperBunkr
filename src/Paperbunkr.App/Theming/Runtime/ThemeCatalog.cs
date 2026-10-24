using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Platform;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Theming.Runtime;

/// <summary>
/// Finds and loads theme families: the built-in ones (embedded <c>Assets/Skins/&lt;key&gt;/theme.json</c>) and the installed ones
/// (<c>ThemePaths.ExtractedDirectory/&lt;key&gt;</c>). The one place that knows where a theme's files are, so the resolver and the installer do not.
/// A built-in key always wins over an installed package of the same name.
/// </summary>
public sealed class ThemeCatalog
{
    public const string DefaultThemeKey = "default";

    public static readonly IReadOnlyList<string> BuiltInKeys = new[]
    {
        DefaultThemeKey, "windows_11", "cool_technical", "vibrant_pop", "vintage_paperback",
        "daylight", "overcast", "maximum_contrast", "colorblind_safe", "matrix",

        // The v2 families (2026-10-13): variants, materials, component skins, and for two of them a Home layout.
        "nordic_frost", "tokyo_midnight", "reading_room", "canopy", "crimson_ink",
        "abyss", "sunset_boulevard", "newsprint", "arcade", "graphite",
    };

    /// <summary>The ten version 1 built-ins, one look each. The rest of <see cref="BuiltInKeys"/> are version 2 families.</summary>
    public const int LegacyBuiltInCount = 10;

    // Built-in documents never change while the app runs; installed ones are re-read when their theme.json changes on disk.
    private static readonly ConcurrentDictionary<string, ThemeDocument?> BuiltInCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Stamp, ThemeDocument? Document)> _installedCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsBuiltIn(string familyKey) => BuiltInKeys.Contains(familyKey, StringComparer.OrdinalIgnoreCase);

    private static string BuiltInRoot(string key) => $"avares://Paperbunkr.App/Assets/Skins/{key}/";

    /// <summary>The family with this key, or null when it does not exist or its <c>theme.json</c> cannot be used. Never throws.</summary>
    public ThemeDocument? Load(string familyKey, List<ThemeDiagnostic>? diagnostics = null)
    {
        if (string.IsNullOrWhiteSpace(familyKey) || familyKey.Contains("..") || familyKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        if (IsBuiltIn(familyKey))
        {
            if (diagnostics is null && BuiltInCache.TryGetValue(familyKey, out var cached))
            {
                return cached;
            }

            var document = Parse(familyKey, ReadBuiltInText(familyKey, "theme.json"), diagnostics, path => ReadBuiltInText(familyKey, path));
            BuiltInCache[familyKey] = document;
            return document;
        }

        string themePath = Path.Combine(ThemePaths.ExtractedDirectory, familyKey, "theme.json");
        try
        {
            if (!File.Exists(themePath))
            {
                return null;
            }

            var stamp = File.GetLastWriteTimeUtc(themePath);
            if (diagnostics is null && _installedCache.TryGetValue(familyKey, out var entry) && entry.Stamp == stamp)
            {
                return entry.Document;
            }

            var document = Parse(familyKey, File.ReadAllText(themePath), diagnostics, path => ReadInstalledText(familyKey, path));
            _installedCache[familyKey] = (stamp, document);
            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Every usable family: the built-ins in their fixed order, then the installed ones by name.</summary>
    public IReadOnlyList<ThemeDocument> LoadAll()
    {
        var result = new List<ThemeDocument>();
        foreach (string key in BuiltInKeys)
        {
            if (Load(key) is { } document)
            {
                result.Add(document);
            }
        }

        foreach (string key in InstalledKeys())
        {
            if (Load(key) is { } document)
            {
                result.Add(document);
            }
        }

        return result;
    }

    public IReadOnlyList<string> InstalledKeys()
    {
        try
        {
            if (!Directory.Exists(ThemePaths.ExtractedDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.GetDirectories(ThemePaths.ExtractedDirectory)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name) && !name.StartsWith('.') && !IsBuiltIn(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Resolves a persisted theme key ("default", "paper:dark") to its family and variant. An unknown variant falls back to the family's first.</summary>
    public bool TryFind(string? themeKey, out ThemeDocument document, out ThemeVariantDefinition variant)
    {
        document = null!;
        variant = null!;
        if (string.IsNullOrWhiteSpace(themeKey))
        {
            return false;
        }

        var (familyKey, variantId) = ThemeDocument.SplitKey(themeKey);
        if (Load(familyKey) is not { } found)
        {
            return false;
        }

        document = found;
        variant = found.FindVariant(variantId) ?? found.DefaultVariant;
        return true;
    }

    /// <summary>Opens a file inside a theme's own package, or null. The path must already be normalized (<see cref="ThemePackagePaths.TryNormalize"/>).</summary>
    public Stream? OpenAsset(string familyKey, string relativePath)
    {
        if (!ThemePackagePaths.TryNormalize(relativePath, out string normalized))
        {
            return null;
        }

        try
        {
            if (IsBuiltIn(familyKey))
            {
                var uri = new Uri(BuiltInRoot(familyKey) + normalized);
                return AssetLoader.Exists(uri) ? AssetLoader.Open(uri) : null;
            }

            string path = Path.Combine(ThemePaths.ExtractedDirectory, familyKey, normalized.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.OpenRead(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The raw <c>theme.json</c> of a family (used by export), or null.</summary>
    public string? ReadThemeJson(string familyKey)
    {
        using var stream = OpenAsset(familyKey, "theme.json");
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Every file in a theme's package as a package-relative path ("theme.json", "icons/app.png"), for export. Empty when the family does not exist.</summary>
    public IReadOnlyList<string> ListFiles(string familyKey)
    {
        try
        {
            if (IsBuiltIn(familyKey))
            {
                string root = BuiltInRoot(familyKey);
                return AssetLoader.GetAssets(new Uri(root), null)
                    .Select(uri => Uri.UnescapeDataString(uri.AbsolutePath))
                    .Select(path => path[(path.IndexOf("/Assets/Skins/" + familyKey + "/", StringComparison.OrdinalIgnoreCase) + ("/Assets/Skins/" + familyKey + "/").Length)..])
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList();
            }

            string folder = Path.Combine(ThemePaths.ExtractedDirectory, familyKey);
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>The folder an installed family lives in, or null for a built-in.</summary>
    public string? InstalledFolder(string familyKey) => IsBuiltIn(familyKey) ? null : Path.Combine(ThemePaths.ExtractedDirectory, familyKey);

    public void Invalidate() => _installedCache.Clear();

    private static ThemeDocument? Parse(string key, string? json, List<ThemeDiagnostic>? diagnostics, Func<string, string?> readPackageFile)
    {
        if (json is null)
        {
            return null;
        }

        return ThemeDocumentParser.Parse(json, key, diagnostics ?? new List<ThemeDiagnostic>(), readPackageFile);
    }

    private static string? ReadBuiltInText(string key, string relativePath)
    {
        try
        {
            var uri = new Uri(BuiltInRoot(key) + relativePath);
            if (!AssetLoader.Exists(uri))
            {
                return null;
            }

            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FileNotFoundException)
        {
            return null;
        }
    }

    private static string? ReadInstalledText(string key, string relativePath)
    {
        try
        {
            string path = Path.Combine(ThemePaths.ExtractedDirectory, key, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
