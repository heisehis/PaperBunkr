using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Media;

namespace Paperbunkr.App.Theming;

/// <summary>
/// A theme variant with every value settled: what the runtime applies and what the inspector explains. Immutable; producing one touches no UI.
/// </summary>
public sealed class ResolvedTheme
{
    internal ResolvedTheme(
        ThemeDocument document,
        ThemeVariantDefinition variant,
        IReadOnlyDictionary<string, ResolvedToken> tokens,
        IReadOnlyList<ThemeDiagnostic> diagnostics,
        IReadOnlyDictionary<string, JsonObject> materials,
        IReadOnlyDictionary<string, JsonObject> motion,
        IReadOnlyDictionary<string, JsonObject> layouts,
        bool trueBlackApplied)
    {
        Document = document;
        Variant = variant;
        Tokens = tokens;
        Diagnostics = diagnostics;
        Materials = materials;
        Motion = motion;
        Layouts = layouts;
        TrueBlackApplied = trueBlackApplied;
    }

    public ThemeDocument Document { get; }

    public ThemeVariantDefinition Variant { get; }

    public string FamilyId => Document.Id;

    public string FamilyKey => Document.Key;

    public string VariantId => Variant.Id;

    /// <summary>The persisted key of this variant (see <see cref="ThemeDocument.ThemeKeyFor"/>).</summary>
    public string ThemeKey => Document.ThemeKeyFor(Variant);

    public string Mode => Variant.Mode;

    public bool IsLight => Variant.IsLight;

    public bool TrueBlackApplied { get; }

    public IReadOnlyDictionary<string, ResolvedToken> Tokens { get; }

    public IReadOnlyList<ThemeDiagnostic> Diagnostics { get; }

    public IReadOnlyDictionary<string, JsonObject> Materials { get; }

    public IReadOnlyDictionary<string, JsonObject> Motion { get; }

    public IReadOnlyDictionary<string, JsonObject> Layouts { get; }

    public ResolvedToken? Token(string path) => Tokens.TryGetValue(ThemeTokenPaths.Normalize(path), out var token) ? token : null;

    public Color? Color(string path) => Token(path) is { Value.Kind: ThemeValueKind.Color } token ? token.Value.Color : null;

    public double? Number(string path) => Token(path) is { Value.Kind: ThemeValueKind.Number } token ? token.Value.Number : null;

    public string? Text(string path) => Token(path) is { Value.Kind: ThemeValueKind.Text } token ? token.Value.Text : null;

    public JsonNode? Json(string path) => Token(path) is { Value.Kind: ThemeValueKind.Json } token ? token.Value.Json : null;

    /// <summary>A component property in a state, falling back to the normal state when the state does not set it.</summary>
    public ResolvedToken? Component(string role, string state, string property) =>
        Token(ThemeTokenPaths.Component(role, state, property)) ?? (state == "normal" ? null : Token(ThemeTokenPaths.Component(role, "normal", property)));

    public IEnumerable<ThemeDiagnostic> Warnings => Diagnostics.Where(d => d.Severity == ThemeDiagnosticSeverity.Warning);

    public IEnumerable<ThemeDiagnostic> Errors => Diagnostics.Where(d => d.Severity == ThemeDiagnosticSeverity.Error);
}

/// <summary>
/// Turns a theme family, a chosen variant, the user's overrides and the environment into a <see cref="ResolvedTheme"/> (design doc section 6).
/// Pure and deterministic: same inputs, same output, no application, no dispatcher. Precedence, lowest to highest: family base, variant, user
/// global, user family, user variant (a component path in a user layer is a component-specific override), the explicit accessibility
/// adjustment, then the legacy True Black transform. A value nothing sets is derived, and a derived value never beats an authored one.
/// </summary>
public static class ThemeResolver
{
    private const int MaxReferenceDepth = 32;

    public static ResolvedTheme Resolve(ThemeDocument document, string? variantId, UserThemeOverrides? overrides = null, ThemeEnvironment? environment = null)
    {
        overrides ??= UserThemeOverrides.Empty;
        environment ??= ThemeEnvironment.Default;
        var diagnostics = new List<ThemeDiagnostic>();

        var variant = document.FindVariant(variantId);
        if (variant is null)
        {
            variant = document.DefaultVariant;
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.MissingVariant,
                $"'{document.Name}' has no variant '{variantId}'; using '{variant.Id}'.", ThemeId: document.Id, VariantId: variantId, FallbackUsed: true));
        }

        var layers = BuildLayers(document, variant, overrides, environment);

        // Pass 1: everything except the accessibility adjustment, which needs resolved colours to know what to adjust.
        var pass = new Pass(document, variant, layers, diagnostics);
        pass.ResolveAll();

        if (overrides.BoostContrast)
        {
            var boost = BuildAccessibilityLayer(pass);
            if (boost.Count > 0)
            {
                // Pass 2 re-derives whatever depended on the adjusted roles; authored values elsewhere are untouched.
                diagnostics.RemoveAll(d => d.Code != ThemeDiagnosticCodes.MissingVariant);

                layers.Insert(IndexBeforeTrueBlack(layers), new Layer(ThemeValueSource.Accessibility, boost));
                pass = new Pass(document, variant, layers, diagnostics);
                pass.ResolveAll();
            }
        }

        AddContrastDiagnostics(document, variant, pass.Tokens, diagnostics);

        return new ResolvedTheme(
            document, variant, pass.Tokens, diagnostics,
            Merge(document.Base.Materials, variant.Layer.Materials),
            Merge(document.Base.Motion, variant.Layer.Motion),
            Merge(document.Base.Layouts, variant.Layer.Layouts, StringComparer.OrdinalIgnoreCase),
            trueBlackApplied: layers.Any(l => l.Source == ThemeValueSource.TrueBlack));
    }

    /// <summary>Whether True Black is a transform for this variant: only a dark, non-OLED variant (a designed OLED variant keeps its own surface hierarchy).</summary>
    public static bool TrueBlackApplies(ThemeVariantDefinition variant, ThemeEnvironment environment) =>
        environment.TrueBlack && !variant.IsLight && variant.Kind != ThemeVariantKind.Oled;

    private static List<Layer> BuildLayers(ThemeDocument document, ThemeVariantDefinition variant, UserThemeOverrides overrides, ThemeEnvironment environment)
    {
        var layers = new List<Layer>
        {
            new(ThemeValueSource.FamilyAuthored, document.Base.Tokens.ToDictionary(p => p.Key, p => new Raw(p.Value.Node, null, p.Value.JsonPath), StringComparer.Ordinal)),
            new(ThemeValueSource.VariantAuthored, variant.Layer.Tokens.ToDictionary(p => p.Key, p => new Raw(p.Value.Node, null, p.Value.JsonPath), StringComparer.Ordinal)),
        };

        var global = FromUser(overrides.Global);
        if (!string.IsNullOrWhiteSpace(environment.AccentOverrideHex) && ThemeColorMath.TryParse(environment.AccentOverrideHex, out _))
        {
            // The pre-v2 global accent setting is the user-global value of the accent role.
            global["action.primary"] = new Raw(null, environment.AccentOverrideHex!.Trim(), "accent override");
        }

        layers.Add(new Layer(ThemeValueSource.UserGlobal, global));

        var family = overrides.FindFamily(document.Id);
        layers.Add(new Layer(ThemeValueSource.UserFamily, FromUser(family?.Family)));
        layers.Add(new Layer(ThemeValueSource.UserVariant, FromUser(family is not null && family.Variants.TryGetValue(variant.Id, out var v) ? v : null)));

        if (TrueBlackApplies(variant, environment))
        {
            layers.Add(new Layer(ThemeValueSource.TrueBlack, ThemeVocabulary.TrueBlackSurfaces.ToDictionary(p => p, _ => new Raw(null, "#000000", "true black"), StringComparer.Ordinal)));
        }

        return layers;
    }

    private static int IndexBeforeTrueBlack(List<Layer> layers)
    {
        int index = layers.FindIndex(l => l.Source == ThemeValueSource.TrueBlack);
        return index < 0 ? layers.Count : index;
    }

    private static Dictionary<string, Raw> FromUser(ThemeOverrideLayer? layer)
    {
        var result = new Dictionary<string, Raw>(StringComparer.Ordinal);
        if (layer is not null)
        {
            foreach (var (path, value) in layer.Values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result[ThemeTokenPaths.Normalize(path)] = new Raw(null, value, "user override");
                }
            }
        }

        return result;
    }

    /// <summary>The text roles raised to at least 4.5:1 on the canvas. Empty when they already pass, so an already-accessible theme is left alone.</summary>
    private static Dictionary<string, Raw> BuildAccessibilityLayer(Pass pass)
    {
        var result = new Dictionary<string, Raw>(StringComparer.Ordinal);
        if (pass.Tokens.TryGetValue("surface.canvas", out var canvasToken) && canvasToken.Value.Kind == ThemeValueKind.Color)
        {
            foreach (string role in new[] { "text.primary", "text.secondary", "text.muted", "text.accent" })
            {
                if (pass.Tokens.TryGetValue(role, out var token) && token.Value.Kind == ThemeValueKind.Color)
                {
                    var adjusted = ThemeColorMath.AdjustForContrast(token.Value.Color, canvasToken.Value.Color, 4.5);
                    if (adjusted != token.Value.Color)
                    {
                        result[role] = new Raw(null, ThemeColorMath.ToHex(adjusted), "contrast boost");
                    }
                }
            }
        }

        return result;
    }

    private static void AddContrastDiagnostics(ThemeDocument document, ThemeVariantDefinition variant, IReadOnlyDictionary<string, ResolvedToken> tokens, List<ThemeDiagnostic> diagnostics)
    {
        foreach (var (foreground, background, target, label) in ThemeVocabulary.ContrastPairs)
        {
            if (!tokens.TryGetValue(foreground, out var fg) || !tokens.TryGetValue(background, out var bg)
                || fg.Value.Kind != ThemeValueKind.Color || bg.Value.Kind != ThemeValueKind.Color)
            {
                continue;
            }

            double ratio = ThemeColorMath.ContrastRatio(fg.Value.Color, bg.Value.Color);
            if (ratio + 0.005 < target)
            {
                diagnostics.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.LowContrast,
                    string.Create(CultureInfo.InvariantCulture, $"{label} is {ratio:0.00}:1 against {background}, below the {target:0.#}:1 target."),
                    foreground, ResolvedValue: fg.Value.ToString(), ThemeId: document.Id, VariantId: variant.Id, Source: fg.Source));
            }
        }
    }

    private static IReadOnlyDictionary<string, JsonObject> Merge(Dictionary<string, JsonObject> baseItems, Dictionary<string, JsonObject> variantItems, StringComparer? comparer = null)
    {
        var merged = new Dictionary<string, JsonObject>(comparer ?? StringComparer.Ordinal);
        foreach (var (name, item) in baseItems)
        {
            merged[name] = item;
        }

        foreach (var (name, item) in variantItems)
        {
            merged[name] = item;
        }

        return merged;
    }

    /// <summary>A raw value in a layer: a JSON node from a theme, or text from a user layer.</summary>
    private sealed record Raw(JsonNode? Node, string? Text, string Origin)
    {
        public string? RefTarget =>
            Node is JsonObject { Count: 1 } obj && obj.TryGetPropertyValue("ref", out var target) && target is JsonValue value && value.TryGetValue(out string? path)
                ? ThemeTokenPaths.Normalize(path)
                : null;
    }

    private sealed record Layer(ThemeValueSource Source, Dictionary<string, Raw> Values);

    private sealed class Pass : IThemeDerivationContext
    {
        private readonly ThemeDocument _document;
        private readonly ThemeVariantDefinition _variant;
        private readonly List<Layer> _layers;
        private readonly List<ThemeDiagnostic> _diagnostics;
        private readonly Dictionary<string, ResolvedToken?> _cache = new(StringComparer.Ordinal);
        private readonly List<string> _resolving = new();

        public Pass(ThemeDocument document, ThemeVariantDefinition variant, List<Layer> layers, List<ThemeDiagnostic> diagnostics)
        {
            _document = document;
            _variant = variant;
            _layers = layers;
            _diagnostics = diagnostics;
        }

        public bool IsLight => _variant.IsLight;

        public Dictionary<string, ResolvedToken> Tokens { get; } = new(StringComparer.Ordinal);

        public void ResolveAll()
        {
            foreach (var role in ThemeVocabulary.Roles)
            {
                Get(role.Path);
            }

            // Everything an author or the user named that is not a known role (palette entries, typography, component properties, custom tokens).
            foreach (string path in _layers.SelectMany(l => l.Values.Keys).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
            {
                Get(path);
            }

            foreach (var (path, token) in _cache)
            {
                if (token is not null)
                {
                    Tokens[path] = token;
                }
            }
        }

        public Color? Color(string path) => Get(ThemeTokenPaths.Normalize(path)) is { Value.Kind: ThemeValueKind.Color } token ? token.Value.Color : null;

        public Color? Palette(params string[] names)
        {
            foreach (string name in names)
            {
                if (Color("palette." + name) is { } color)
                {
                    return color;
                }
            }

            return null;
        }

        private ResolvedToken? Get(string path)
        {
            if (_cache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            if (_resolving.Contains(path))
            {
                string chain = string.Join(" -> ", _resolving.SkipWhile(p => p != path).Append(path));
                Report(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.ReferenceCycle, $"These values refer to each other in a loop: {chain}.", path, fallback: true);
                return null;
            }

            if (_resolving.Count >= MaxReferenceDepth)
            {
                Report(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.LimitExceeded, $"References are nested more than {MaxReferenceDepth} deep.", path, fallback: true);
                return null;
            }

            _resolving.Add(path);
            try
            {
                var role = ThemeVocabulary.Find(path);
                var token = FromLayers(path, role) ?? Derive(path, role);
                _cache[path] = token;
                return token;
            }
            finally
            {
                _resolving.RemoveAt(_resolving.Count - 1);
            }
        }

        private ResolvedToken? Derive(string path, ThemeRole? role)
        {
            var derived = role?.Derive?.Invoke(this);
            return derived is { } value ? new ResolvedToken(path, value, ThemeValueSource.Derived, "derived") : null;
        }

        private ResolvedToken? FromLayers(string path, ThemeRole? role)
        {
            int accentRank = ThemeVocabulary.AccentDependents.Contains(path) ? RankOf("action.primary") : -1;

            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                var layer = _layers[i];
                if (!layer.Values.TryGetValue(path, out var raw))
                {
                    continue;
                }

                // A user accent supersedes the accent-tuned values a lower layer authored for the theme's own accent.
                if (accentRank > i && _layers[accentRank].Source >= ThemeValueSource.UserGlobal && _layers[accentRank].Source <= ThemeValueSource.UserVariant)
                {
                    return null;
                }

                var source = layer.Source is >= ThemeValueSource.UserGlobal and <= ThemeValueSource.UserVariant && ThemeTokenPaths.IsComponent(path)
                    ? ThemeValueSource.UserComponent
                    : layer.Source;

                if (Read(path, role, raw, source) is { } token)
                {
                    return token;
                }

                // An unusable value in this layer: fall through to a lower layer, then to derivation.
            }

            return null;
        }

        private int RankOf(string path)
        {
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                if (_layers[i].Values.ContainsKey(path))
                {
                    return i;
                }
            }

            return -1;
        }

        private ResolvedToken? Read(string path, ThemeRole? role, Raw raw, ThemeValueSource source)
        {
            if (raw.RefTarget is { } target)
            {
                var referenced = Get(target);
                if (referenced is null)
                {
                    if (!_diagnostics.Any(d => d.Code == ThemeDiagnosticCodes.ReferenceCycle && d.PropertyPath == target))
                    {
                        Report(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.UnknownReference, $"'{raw.Origin}' refers to '{target}', which is not defined.", path, authored: target, fallback: true);
                    }

                    return null;
                }

                if (role is not null && referenced.Value.Kind != role.Kind)
                {
                    Report(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.TypeMismatch,
                        $"'{path}' expects a {Describe(role.Kind)} but '{target}' is a {Describe(referenced.Value.Kind)}.", path, authored: target, fallback: true);
                    return null;
                }

                return new ResolvedToken(path, referenced.Value, source, $"ref {target}");
            }

            ThemeValue? value = raw.Text is not null ? ThemeValue.FromText(raw.Text, role?.Kind) : ThemeValue.FromLiteral(raw.Node, role?.Kind);
            if (value is not { } literal)
            {
                return null;
            }

            if (role is not null && literal.Kind != role.Kind)
            {
                Report(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.TypeMismatch,
                    $"'{path}' expects a {Describe(role.Kind)} but '{literal}' is not one.", path, authored: literal.ToString(), fallback: true);
                return null;
            }

            return new ResolvedToken(path, literal, source);
        }

        private static string Describe(ThemeValueKind kind) => kind switch
        {
            ThemeValueKind.Color => "colour",
            ThemeValueKind.Number => "number",
            ThemeValueKind.Flag => "true/false value",
            ThemeValueKind.Json => "structured value",
            _ => "text value",
        };

        private void Report(ThemeDiagnosticSeverity severity, string code, string message, string path, string? authored = null, bool fallback = false) =>
            _diagnostics.Add(new ThemeDiagnostic(severity, code, message, path, authored, ThemeId: _document.Id, VariantId: _variant.Id, FallbackUsed: fallback));
    }
}
