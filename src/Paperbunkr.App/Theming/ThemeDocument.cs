using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Paperbunkr.App.Theming;

/// <summary>What kind of variant this is. <see cref="Oled"/> uses dark native chrome but is its own designed variant, not "every surface black".</summary>
public enum ThemeVariantKind
{
    Light,
    Dark,
    Oled,
    HighContrast,
}

/// <summary>A value exactly as an author wrote it, with the JSON path it came from (for error messages). Either a literal or a <c>{ "ref": "..." }</c>.</summary>
public readonly record struct ThemeRawEntry(JsonNode Node, string JsonPath)
{
    /// <summary>The target path when this entry is a reference object, else null.</summary>
    public string? RefTarget =>
        Node is JsonObject { Count: 1 } obj && obj.TryGetPropertyValue("ref", out var target) && target is JsonValue value && value.TryGetValue(out string? path)
            ? ThemeTokenPaths.Normalize(path)
            : null;
}

/// <summary>Path helpers shared by the parser, the resolver and the override store.</summary>
public static class ThemeTokenPaths
{
    public const string ComponentPrefix = "component:";

    /// <summary>"semantic.surface.canvas" and "surface.canvas" name the same role; the engine stores roles without the "semantic." prefix.</summary>
    public static string Normalize(string? path)
    {
        string trimmed = (path ?? string.Empty).Trim();
        return trimmed.StartsWith("semantic.", StringComparison.Ordinal) ? trimmed["semantic.".Length..] : trimmed;
    }

    /// <summary>The flat key of one component property in one state, e.g. <c>component:button.primary:hover:background</c>.</summary>
    public static string Component(string role, string state, string property) => $"{ComponentPrefix}{role}:{state}:{property}";

    public static bool IsComponent(string path) => path.StartsWith(ComponentPrefix, StringComparison.Ordinal);

    public static bool TryParseComponent(string path, out string role, out string state, out string property)
    {
        role = state = property = string.Empty;
        if (!IsComponent(path))
        {
            return false;
        }

        string[] parts = path[ComponentPrefix.Length..].Split(':');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        (role, state, property) = (parts[0], parts[1], parts[2]);
        return true;
    }
}

/// <summary>
/// One set of authored values: either the family's shared base or one variant's differences. Token-like sections are flattened to dotted paths
/// (so inheritance, overrides and references all work on one dictionary); structured sections (materials, motion, layouts) stay as JSON.
/// </summary>
public sealed class ThemeLayer
{
    public Dictionary<string, ThemeRawEntry> Tokens { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, JsonObject> Materials { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, JsonObject> Motion { get; } = new(StringComparer.Ordinal);

    /// <summary>Screen name ("Home") to its layout tree.</summary>
    public Dictionary<string, JsonObject> Layouts { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ThemeVariantDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>"Light" or "Dark" - which native control chrome this variant needs.</summary>
    public required string Mode { get; init; }

    public required ThemeVariantKind Kind { get; init; }

    public ThemeLayer Layer { get; init; } = new();

    public bool IsLight => string.Equals(Mode, "Light", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A theme family as loaded (design doc sections 5.3 and 5.4): shared base values plus one or more variants that declare only their differences.
/// A legacy <c>theme.json</c> (no <c>schemaVersion</c>) becomes a family with one variant, in memory only - the file is never rewritten.
/// </summary>
public sealed class ThemeDocument
{
    public const int CurrentSchemaVersion = 2;

    public const string DefaultVariantId = "default";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Stable identity across updates (reverse-DNS style for v2; "legacy.&lt;key&gt;" for an adapted v1 theme). User overrides are keyed by it.</summary>
    public required string Id { get; init; }

    /// <summary>The install key: the built-in folder name or the package's file name.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? Category { get; init; }

    public bool IsLegacy { get; init; }

    public ThemeLayer Base { get; init; } = new();

    public List<ThemeVariantDefinition> Variants { get; init; } = new();

    public ThemeVariantDefinition? FindVariant(string? id) =>
        string.IsNullOrEmpty(id) ? Variants.FirstOrDefault() : Variants.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    public ThemeVariantDefinition DefaultVariant => Variants[0];

    public bool HasMultipleVariants => Variants.Count > 1;

    /// <summary>The persisted key of one variant: the bare family key for a single-variant family (so every existing key stays valid), else <c>family:variant</c>.</summary>
    public string ThemeKeyFor(ThemeVariantDefinition variant) => HasMultipleVariants ? ComposeKey(Key, variant.Id) : Key;

    public static string ComposeKey(string familyKey, string variantId) => $"{familyKey}:{variantId}";

    /// <summary>Splits a persisted theme key into its family key and (possibly absent) variant id.</summary>
    public static (string FamilyKey, string? VariantId) SplitKey(string themeKey)
    {
        int colon = themeKey.IndexOf(':');
        return colon < 0 ? (themeKey, null) : (themeKey[..colon], themeKey[(colon + 1)..]);
    }
}

/// <summary>
/// Reads a <c>theme.json</c> into a <see cref="ThemeDocument"/>: detects the schema, adapts legacy files, flattens the token sections and
/// collects diagnostics with JSON paths. Pure - it is handed a callback for files a v2 theme references inside its own package.
/// </summary>
public static class ThemeDocumentParser
{
    /// <summary>Guards against a pathological package rather than an honest large one (design doc section 5.5).</summary>
    public const int MaxTokens = 4000;
    public const int MaxDepth = 16;
    public const int MaxVariants = 8;

    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 };

    private static readonly JsonSerializerOptions LegacyOptions = new() { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };

    private static readonly string[] TokenSections = { "palette", "semantic", "metrics", "settings", "typography", "icons", "surfaces" };

    /// <summary>
    /// Returns null (with an error diagnostic) when the file cannot be used at all. <paramref name="readPackageFile"/> resolves a normalized
    /// relative path inside the theme's own package to its text, or null when it does not exist; it is only called for v2 layout / motion files.
    /// </summary>
    public static ThemeDocument? Parse(string json, string key, List<ThemeDiagnostic> diagnostics, Func<string, string?>? readPackageFile = null)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: DocumentOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.InvalidJson, $"theme.json is not valid JSON: {ex.Message}", ThemeId: key));
            return null;
        }

        if (root is not JsonObject obj)
        {
            diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.InvalidJson, "theme.json must be a JSON object.", ThemeId: key));
            return null;
        }

        if (!obj.TryGetPropertyValue("schemaVersion", out var versionNode) || versionNode is null)
        {
            return ParseLegacy(json, key, diagnostics);
        }

        int version = versionNode is JsonValue v && v.TryGetValue(out int parsed) ? parsed : -1;
        if (version == 1)
        {
            return ParseLegacy(json, key, diagnostics);
        }

        if (version != ThemeDocument.CurrentSchemaVersion)
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.UnsupportedSchema,
                $"This theme uses schema version {versionNode.ToJsonString()}, and this version of Paperbunkr understands up to {ThemeDocument.CurrentSchemaVersion}.",
                "schemaVersion", ThemeId: key));
            return null;
        }

        return ParseV2(obj, key, diagnostics, readPackageFile);
    }

    private static ThemeDocument? ParseLegacy(string json, string key, List<ThemeDiagnostic> diagnostics)
    {
        try
        {
            var legacy = JsonSerializer.Deserialize<Models.ThemeDefinition>(json, LegacyOptions);
            if (legacy is null)
            {
                diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.InvalidJson, "theme.json is empty.", ThemeId: key));
                return null;
            }

            return LegacyThemeAdapter.ToDocument(key, legacy);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.InvalidJson, $"theme.json could not be read: {ex.Message}", ThemeId: key));
            return null;
        }
    }

    private static ThemeDocument? ParseV2(JsonObject root, string key, List<ThemeDiagnostic> diagnostics, Func<string, string?>? readPackageFile)
    {
        string id = ReadString(root, "id") ?? $"local.{key}";
        string name = ReadString(root, "name") ?? key;
        var context = new ParseContext(id, diagnostics, readPackageFile);

        var document = new ThemeDocument
        {
            SchemaVersion = ThemeDocument.CurrentSchemaVersion,
            Id = id,
            Key = key,
            Name = name,
            Category = ReadString(root, "category"),
        };

        if (root["base"] is JsonObject baseObject)
        {
            ReadLayer(baseObject, "base", document.Base, context);
        }

        if (root["variants"] is JsonObject variants)
        {
            foreach (var (variantId, node) in variants)
            {
                if (document.Variants.Count >= MaxVariants)
                {
                    context.Error(ThemeDiagnosticCodes.LimitExceeded, $"A theme may declare at most {MaxVariants} variants; the rest are ignored.", "variants");
                    break;
                }

                if (node is not JsonObject variantObject || string.IsNullOrWhiteSpace(variantId) || variantId.Contains(':'))
                {
                    context.Error(ThemeDiagnosticCodes.InvalidValue, $"Variant '{variantId}' must be an object and its id may not contain ':'.", $"variants.{variantId}");
                    continue;
                }

                string mode = NormalizeMode(ReadString(variantObject, "mode"), variantId);
                var variant = new ThemeVariantDefinition
                {
                    Id = variantId,
                    Name = ReadString(variantObject, "name") ?? DefaultVariantName(variantId),
                    Mode = mode,
                    Kind = ReadKind(ReadString(variantObject, "kind"), variantId, mode),
                };
                ReadLayer(variantObject, $"variants.{variantId}", variant.Layer, context);
                document.Variants.Add(variant);
            }
        }

        if (document.Variants.Count == 0)
        {
            // A family may ship a single look without spelling out a variants block.
            string mode = NormalizeMode(ReadString(root, "mode"), ThemeDocument.DefaultVariantId);
            document.Variants.Add(new ThemeVariantDefinition
            {
                Id = ThemeDocument.DefaultVariantId,
                Name = name,
                Mode = mode,
                Kind = ReadKind(null, ThemeDocument.DefaultVariantId, mode),
            });
        }

        int tokenCount = document.Base.Tokens.Count + document.Variants.Sum(v => v.Layer.Tokens.Count);
        if (tokenCount > MaxTokens)
        {
            context.Error(ThemeDiagnosticCodes.LimitExceeded, $"The theme declares {tokenCount} values; the limit is {MaxTokens}.", null);
            return null;
        }

        return document;
    }

    private static void ReadLayer(JsonObject source, string jsonPath, ThemeLayer layer, ParseContext context)
    {
        foreach (string section in TokenSections)
        {
            if (source[section] is JsonObject sectionObject)
            {
                string prefix = section == "semantic" ? string.Empty : section + ".";
                Flatten(sectionObject, prefix, $"{jsonPath}.{section}", layer, context, depth: 0);
            }
        }

        if (source["components"] is JsonObject components)
        {
            foreach (var (role, node) in components)
            {
                if (node is JsonObject component)
                {
                    ReadComponent(role, component, $"{jsonPath}.components.{role}", layer, context);
                }
            }
        }

        ReadNamedObjects(source, "materials", jsonPath, layer.Materials, context);
        ReadNamedObjects(source, "motion", jsonPath, layer.Motion, context);
        ReadLayouts(source, jsonPath, layer, context);
    }

    private static void Flatten(JsonObject source, string prefix, string jsonPath, ThemeLayer layer, ParseContext context, int depth)
    {
        if (depth > MaxDepth)
        {
            context.Error(ThemeDiagnosticCodes.LimitExceeded, $"Values are nested more than {MaxDepth} levels deep.", jsonPath);
            return;
        }

        foreach (var (name, node) in source)
        {
            if (node is null)
            {
                continue;
            }

            string path = prefix + name;
            string childJsonPath = $"{jsonPath}.{name}";
            bool isRef = node is JsonObject { Count: 1 } single && single.ContainsKey("ref");
            if (node is JsonObject child && !isRef)
            {
                Flatten(child, path + ".", childJsonPath, layer, context, depth + 1);
            }
            else
            {
                layer.Tokens[path] = new ThemeRawEntry(node.DeepClone(), childJsonPath);
            }
        }
    }

    private static void ReadComponent(string role, JsonObject component, string jsonPath, ThemeLayer layer, ParseContext context)
    {
        if (role.Contains(':'))
        {
            context.Error(ThemeDiagnosticCodes.InvalidValue, $"Component role '{role}' may not contain ':'.", jsonPath);
            return;
        }

        foreach (var (property, node) in component)
        {
            if (node is null)
            {
                continue;
            }

            if (property == "states" && node is JsonObject states)
            {
                foreach (var (state, stateNode) in states)
                {
                    if (stateNode is JsonObject stateObject)
                    {
                        foreach (var (stateProperty, value) in stateObject)
                        {
                            if (value is not null)
                            {
                                layer.Tokens[ThemeTokenPaths.Component(role, state, stateProperty)] = new ThemeRawEntry(value.DeepClone(), $"{jsonPath}.states.{state}.{stateProperty}");
                            }
                        }
                    }
                }

                continue;
            }

            layer.Tokens[ThemeTokenPaths.Component(role, "normal", property)] = new ThemeRawEntry(node.DeepClone(), $"{jsonPath}.{property}");
        }
    }

    private static void ReadNamedObjects(JsonObject source, string section, string jsonPath, Dictionary<string, JsonObject> target, ParseContext context)
    {
        if (source[section] is not JsonObject sectionObject)
        {
            return;
        }

        foreach (var (name, node) in sectionObject)
        {
            if (node is JsonObject item)
            {
                target[name] = (JsonObject)item.DeepClone();
            }
            else
            {
                context.Error(ThemeDiagnosticCodes.InvalidValue, $"'{name}' must be an object.", $"{jsonPath}.{section}.{name}");
            }
        }
    }

    private static void ReadLayouts(JsonObject source, string jsonPath, ThemeLayer layer, ParseContext context)
    {
        if (source["layouts"] is not JsonObject layouts)
        {
            return;
        }

        foreach (var (screen, node) in layouts)
        {
            string layoutJsonPath = $"{jsonPath}.layouts.{screen}";
            JsonObject? tree = node as JsonObject;

            // A string names a file inside the package ("layouts/home.json").
            if (tree is null && node is JsonValue value && value.TryGetValue(out string? file) && file is not null)
            {
                if (!ThemePackagePaths.TryNormalize(file, out string normalized))
                {
                    context.Error(ThemeDiagnosticCodes.UnsafePackage, $"'{file}' is not a path inside the theme package.", layoutJsonPath);
                    continue;
                }

                string? text = context.ReadPackageFile?.Invoke(normalized);
                if (text is null)
                {
                    context.Error(ThemeDiagnosticCodes.InvalidAsset, $"The layout file '{normalized}' is missing from the package.", layoutJsonPath);
                    continue;
                }

                try
                {
                    tree = JsonNode.Parse(text, documentOptions: DocumentOptions) as JsonObject;
                }
                catch (JsonException ex)
                {
                    context.Error(ThemeDiagnosticCodes.InvalidJson, $"The layout file '{normalized}' is not valid JSON: {ex.Message}", layoutJsonPath);
                    continue;
                }
            }

            if (tree is null)
            {
                context.Error(ThemeDiagnosticCodes.InvalidLayout, "A layout must be an object or the path of a JSON file in the package.", layoutJsonPath);
                continue;
            }

            layer.Layouts[screen] = (JsonObject)tree.DeepClone();
        }
    }

    private static string? ReadString(JsonObject source, string name) =>
        source.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static string NormalizeMode(string? mode, string variantId)
    {
        if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase))
        {
            return "Light";
        }

        if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return "Dark";
        }

        // No mode written: a variant called "light" is light; everything else gets dark chrome (the engine-wide default).
        return variantId.Contains("light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
    }

    private static ThemeVariantKind ReadKind(string? kind, string variantId, string mode)
    {
        string probe = (kind ?? variantId).Replace("-", string.Empty).Replace("_", string.Empty);
        if (probe.Contains("oled", StringComparison.OrdinalIgnoreCase) || probe.Contains("trueblack", StringComparison.OrdinalIgnoreCase))
        {
            return ThemeVariantKind.Oled;
        }

        if (probe.Contains("contrast", StringComparison.OrdinalIgnoreCase))
        {
            return ThemeVariantKind.HighContrast;
        }

        return mode == "Light" ? ThemeVariantKind.Light : ThemeVariantKind.Dark;
    }

    private static string DefaultVariantName(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..].Replace('_', ' ').Replace('-', ' ');

    private sealed class ParseContext
    {
        private readonly List<ThemeDiagnostic> _diagnostics;
        private readonly string _themeId;

        public ParseContext(string themeId, List<ThemeDiagnostic> diagnostics, Func<string, string?>? readPackageFile)
        {
            _themeId = themeId;
            _diagnostics = diagnostics;
            ReadPackageFile = readPackageFile;
        }

        public Func<string, string?>? ReadPackageFile { get; }

        public void Error(string code, string message, string? path) =>
            _diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, code, message, path, ThemeId: _themeId));
    }
}

/// <summary>Validation of paths that a theme uses to point at files inside its own package (design doc section 5.2).</summary>
public static class ThemePackagePaths
{
    /// <summary>
    /// True for a relative path that stays inside the package: no drive or root, no <c>..</c> segment, no URL scheme, no empty segment.
    /// <paramref name="normalized"/> uses forward slashes.
    /// </summary>
    public static bool TryNormalize(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string candidate = path.Trim().Replace('\\', '/');
        if (candidate.StartsWith('/') || candidate.Contains(':') || candidate.Contains('\0'))
        {
            return false;
        }

        string[] segments = candidate.Split('/');
        if (segments.Any(s => s.Length == 0 || s == "." || s == ".." || s.EndsWith(' ') || s.EndsWith('.')))
        {
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }
}
