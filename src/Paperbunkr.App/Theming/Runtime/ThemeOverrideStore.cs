using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Paperbunkr.App.Theming.Runtime;

/// <summary>Where the user's theme overrides are kept. A file in the app, memory in tests.</summary>
public interface IThemeOverrideStore
{
    /// <summary>The current overrides. Never null; an unreadable store yields an empty set.</summary>
    UserThemeOverrides Load();

    void Save(UserThemeOverrides overrides);
}

/// <summary>Keeps overrides in memory only - what a <c>ThemeService</c> built for a test uses, so a test can never read or write the developer's real file.</summary>
public sealed class MemoryThemeOverrideStore : IThemeOverrideStore
{
    private UserThemeOverrides _current = new();

    public UserThemeOverrides Load() => _current;

    public void Save(UserThemeOverrides overrides) => _current = overrides;
}

/// <summary>
/// <c>theme-overrides.json</c> beside the database (design doc decision D2): the user's global, per-family and per-variant values, layout
/// patches, the contrast boost and the effect quality. Follows the <c>keymap.json</c> conventions - a versioned document, written to a
/// temporary file and swapped in, and a file that cannot be read (invalid JSON, a newer version) is set aside as <c>.bad</c> rather than
/// overwritten or allowed to break startup. Installed theme packages are never touched.
/// </summary>
public sealed class FileThemeOverrideStore : IThemeOverrideStore
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _path;
    private UserThemeOverrides? _cached;

    public FileThemeOverrideStore(string path) => _path = path;

    public string FilePath => _path;

    public static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(Paperbunkr.Data.PaperbunkrDbContext.GetDefaultDatabasePath()) ?? ".", "theme-overrides.json");

    public UserThemeOverrides Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        _cached = Read();
        return _cached;
    }

    public void Save(UserThemeOverrides overrides)
    {
        overrides.Compact();
        _cached = overrides;
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = _path + ".tmp";
            File.WriteAllText(temp, ThemeOverrideSerializer.Serialize(overrides).ToJsonString(WriteOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The in-memory copy still applies for this session; the next successful save persists it.
        }
    }

    private UserThemeOverrides Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new UserThemeOverrides();
            }

            var root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
            if (root is null || ThemeOverrideSerializer.Deserialize(root) is not { } overrides)
            {
                SetAside();
                return new UserThemeOverrides();
            }

            return overrides;
        }
        catch (JsonException)
        {
            SetAside();
            return new UserThemeOverrides();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new UserThemeOverrides();
        }
    }

    /// <summary>Keeps an unreadable file as evidence and for manual recovery instead of silently replacing it on the next save.</summary>
    private void SetAside()
    {
        try
        {
            File.Move(_path, _path + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>The JSON shape of the override file (also what "Export my customizations" writes). Pure.</summary>
public static class ThemeOverrideSerializer
{
    public static JsonObject Serialize(UserThemeOverrides overrides)
    {
        var root = new JsonObject
        {
            ["version"] = FileThemeOverrideStore.CurrentVersion,
            ["boostContrast"] = overrides.BoostContrast,
            ["effectQuality"] = overrides.EffectQuality.ToString(),
            ["allowThemeLayouts"] = overrides.AllowThemeLayouts,
            ["global"] = WriteLayer(overrides.Global),
        };

        var families = new JsonObject();
        foreach (var (id, family) in overrides.Families.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var familyObject = new JsonObject { ["values"] = WriteLayer(family.Family) };

            var variants = new JsonObject();
            foreach (var (variantId, layer) in family.Variants.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                variants[variantId] = WriteLayer(layer);
            }

            familyObject["variants"] = variants;

            var layouts = new JsonObject();
            foreach (var (screen, patches) in family.LayoutPatches.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var screenObject = new JsonObject();
                foreach (var (nodeId, patch) in patches.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    screenObject[nodeId] = patch.DeepClone();
                }

                layouts[screen] = screenObject;
            }

            familyObject["layouts"] = layouts;
            families[id] = familyObject;
        }

        root["families"] = families;
        return root;
    }

    /// <summary>Null when the document is not one this version understands (wrong shape, or written by a newer version).</summary>
    public static UserThemeOverrides? Deserialize(JsonObject root)
    {
        if (root["version"] is not JsonValue versionNode || !versionNode.TryGetValue(out int version) || version < 1 || version > FileThemeOverrideStore.CurrentVersion)
        {
            return null;
        }

        var overrides = new UserThemeOverrides
        {
            BoostContrast = root["boostContrast"] is JsonValue boost && boost.TryGetValue(out bool flag) && flag,
            AllowThemeLayouts = root["allowThemeLayouts"] is JsonValue allow && allow.TryGetValue(out bool allowFlag) && allowFlag,
            EffectQuality = root["effectQuality"] is JsonValue quality && quality.TryGetValue(out string? text) && Enum.TryParse<ThemeEffectQuality>(text, ignoreCase: true, out var parsed)
                ? parsed
                : ThemeEffectQuality.High,
        };

        ReadLayer(root["global"], overrides.Global);

        if (root["families"] is JsonObject families)
        {
            foreach (var (id, node) in families)
            {
                if (node is not JsonObject familyObject)
                {
                    continue;
                }

                var family = overrides.GetOrAddFamily(id);
                ReadLayer(familyObject["values"], family.Family);

                if (familyObject["variants"] is JsonObject variants)
                {
                    foreach (var (variantId, layerNode) in variants)
                    {
                        var layer = new ThemeOverrideLayer();
                        ReadLayer(layerNode, layer);
                        family.Variants[variantId] = layer;
                    }
                }

                if (familyObject["layouts"] is JsonObject layouts)
                {
                    foreach (var (screen, screenNode) in layouts)
                    {
                        if (screenNode is not JsonObject screenObject)
                        {
                            continue;
                        }

                        var patches = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
                        foreach (var (nodeId, patch) in screenObject)
                        {
                            if (patch is JsonObject patchObject)
                            {
                                patches[nodeId] = (JsonObject)patchObject.DeepClone();
                            }
                        }

                        family.LayoutPatches[screen] = patches;
                    }
                }
            }
        }

        return overrides;
    }

    private static JsonObject WriteLayer(ThemeOverrideLayer layer)
    {
        var result = new JsonObject();
        foreach (var (path, value) in layer.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            result[path] = value;
        }

        return result;
    }

    private static void ReadLayer(JsonNode? node, ThemeOverrideLayer layer)
    {
        if (node is not JsonObject values)
        {
            return;
        }

        foreach (var (path, value) in values)
        {
            // Values are stored as text; a number or bool written by hand is accepted too.
            string? text = value switch
            {
                JsonValue v when v.TryGetValue(out string? s) => s,
                JsonValue v => v.ToJsonString(),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                layer.Values[path] = text;
            }
        }
    }
}
