using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Theming;

/// <summary>
/// The bridge between the original <c>theme.json</c> shape (<see cref="ThemeDefinition"/>, no <c>schemaVersion</c>) and the v2 engine
/// (design doc section 5.1). A legacy theme becomes a single-variant family in memory - every one of its values authored explicitly, so the
/// resolver reproduces it exactly - and the file on disk is never rewritten. The reverse direction lets code that still wants a
/// <see cref="ThemeDefinition"/> (preview cards, the Matrix overlay) keep working for v2 themes.
/// </summary>
public static class LegacyThemeAdapter
{
    /// <summary>Legacy colour field name to the role it sets. The order is the legacy file's own.</summary>
    private static readonly (string Role, Func<SkinColors, string?> Read)[] ColorFields =
    {
        ("surface.canvas", c => c.Bg),
        ("surface.navigation", c => c.Chrome),
        ("border.default", c => c.Border),
        ("text.primary", c => c.Text),
        ("text.secondary", c => c.TextMuted),
        ("text.muted", c => c.TextFaint),
        ("action.primary", c => c.Accent),
        ("text.accent", c => c.AccentText),
        ("action.soft", c => c.AccentSoft),
        ("badge.background", c => c.Badge),
        ("badge.text", c => c.BadgeText),
        ("status.success", c => c.Success),
        ("chart.series.1", c => c.ChartBlue),
        ("chart.series.2", c => c.ChartViolet),
        ("surface.recessed", c => c.Surface0),
        ("surface.default", c => c.Surface1),
        ("surface.raised", c => c.Surface2),
        ("surface.overlay", c => c.Surface3),
        ("glow.color", c => c.Glow),
        ("hero.gradient.start", c => c.HeroGradientStart),
        ("hero.gradient.end", c => c.HeroGradientEnd),
        ("era.platinum", c => c.EraPlatinum),
        ("era.golden", c => c.EraGolden),
        ("era.silver", c => c.EraSilver),
        ("era.bronze", c => c.EraBronze),
        ("era.modern", c => c.EraModern),
    };

    public static string LegacyId(string key) => "legacy." + key;

    public static ThemeDocument ToDocument(string key, ThemeDefinition legacy)
    {
        bool isLight = string.Equals(legacy.Mode, "Light", StringComparison.OrdinalIgnoreCase);
        var variant = new ThemeVariantDefinition
        {
            Id = ThemeDocument.DefaultVariantId,
            Name = legacy.Name,
            Mode = isLight ? "Light" : "Dark",
            Kind = isLight ? ThemeVariantKind.Light : ThemeVariantKind.Dark,
        };

        var document = new ThemeDocument
        {
            SchemaVersion = 1,
            Id = LegacyId(key),
            Key = key,
            Name = legacy.Name,
            Category = legacy.Category,
            IsLegacy = true,
        };
        document.Variants.Add(variant);

        var tokens = document.Base.Tokens;
        foreach (var (role, read) in ColorFields)
        {
            if (read(legacy.Colors) is { } hex)
            {
                tokens[role] = Entry(JsonValue.Create(hex), $"colors.{role}");
            }
        }

        tokens["metrics.spacingUnit"] = Entry(JsonValue.Create(legacy.SpacingUnit), "spacingUnit");
        tokens["metrics.radius"] = Entry(JsonValue.Create(legacy.Radius), "radius");
        tokens["metrics.radiusSm"] = Entry(JsonValue.Create(legacy.RadiusSm), "radiusSm");
        tokens["metrics.radiusLg"] = Entry(JsonValue.Create(legacy.RadiusLg), "radiusLg");

        if (legacy.ScrollbarWidth is { } width)
        {
            tokens["metrics.scrollbarWidth"] = Entry(JsonValue.Create(width), "scrollbarWidth");
        }

        if (legacy.ScrollbarThumbOpacity is { } opacity)
        {
            tokens["metrics.scrollbarThumbOpacity"] = Entry(JsonValue.Create(opacity), "scrollbarThumbOpacity");
        }

        tokens["settings.windowBackdrop"] = Entry(JsonValue.Create(legacy.WindowBackdrop ?? "None"), "windowBackdrop");

        if (legacy.MatrixGlyphs is { Length: > 0 } glyphs)
        {
            var array = new JsonArray();
            foreach (string glyph in glyphs)
            {
                array.Add(glyph);
            }

            tokens["settings.matrixGlyphs"] = Entry(array, "matrixGlyphs");
        }

        if (!string.IsNullOrWhiteSpace(legacy.DefaultFontFamily))
        {
            tokens["typography.body.family"] = Entry(JsonValue.Create(legacy.DefaultFontFamily), "defaultFontFamily");
        }

        foreach (var (iconKey, path) in legacy.Icons)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                tokens["icons." + iconKey] = Entry(JsonValue.Create(path), $"icons.{iconKey}");
            }
        }

        return document;
    }

    /// <summary>A <see cref="ThemeDefinition"/> describing a resolved theme, for the callers that predate the v2 model.</summary>
    public static ThemeDefinition FromResolved(ResolvedTheme theme)
    {
        string Hex(string role, string fallback) => theme.Color(role) is { } color ? ThemeColorMath.ToHex(color) : fallback;
        string? HexOrNull(string role) => theme.Token(role) is { IsDerived: false } token && token.Value.Kind == ThemeValueKind.Color ? ThemeColorMath.ToHex(token.Value.Color) : null;

        var defaults = new SkinColors();
        var definition = new ThemeDefinition
        {
            Name = theme.Document.HasMultipleVariants ? $"{theme.Document.Name} · {theme.Variant.Name}" : theme.Document.Name,
            Mode = theme.Mode,
            Category = theme.Document.Category,
            DefaultFontFamily = theme.Text("typography.body.family"),
            WindowBackdrop = theme.Text("settings.windowBackdrop") ?? "None",
            ScrollbarWidth = theme.Number("metrics.scrollbarWidth"),
            ScrollbarThumbOpacity = theme.Number("metrics.scrollbarThumbOpacity"),
            SpacingUnit = theme.Number("metrics.spacingUnit") ?? 4,
            Radius = theme.Number("metrics.radius") ?? 7,
            RadiusSm = theme.Number("metrics.radiusSm") ?? 5,
            RadiusLg = theme.Number("metrics.radiusLg") ?? 14,
            Colors = new SkinColors
            {
                Bg = Hex("surface.canvas", defaults.Bg),
                Chrome = Hex("surface.navigation", defaults.Chrome),
                Border = Hex("border.default", defaults.Border),
                Text = Hex("text.primary", defaults.Text),
                TextMuted = Hex("text.secondary", defaults.TextMuted),
                TextFaint = Hex("text.muted", defaults.TextFaint),
                Accent = Hex("action.primary", defaults.Accent),
                AccentText = Hex("text.accent", defaults.AccentText),
                AccentSoft = Hex("action.soft", defaults.AccentSoft),
                Badge = Hex("badge.background", defaults.Badge),
                BadgeText = Hex("badge.text", defaults.BadgeText),
                Success = Hex("status.success", defaults.Success),
                ChartBlue = Hex("chart.series.1", defaults.ChartBlue),
                ChartViolet = Hex("chart.series.2", defaults.ChartViolet),
                Surface0 = Hex("surface.recessed", defaults.Surface0),
                Surface1 = Hex("surface.default", defaults.Surface1),
                Surface2 = Hex("surface.raised", defaults.Surface2),
                Surface3 = Hex("surface.overlay", defaults.Surface3),
                Glow = Hex("glow.color", defaults.Glow),
                HeroGradientStart = Hex("hero.gradient.start", defaults.HeroGradientStart),
                HeroGradientEnd = Hex("hero.gradient.end", defaults.HeroGradientEnd),
                EraPlatinum = HexOrNull("era.platinum"),
                EraGolden = HexOrNull("era.golden"),
                EraSilver = HexOrNull("era.silver"),
                EraBronze = HexOrNull("era.bronze"),
                EraModern = HexOrNull("era.modern"),
            },
        };

        if (theme.Json("settings.matrixGlyphs") is JsonArray glyphs)
        {
            var list = new List<string>();
            foreach (var glyph in glyphs)
            {
                if (glyph is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrEmpty(text))
                {
                    list.Add(text);
                }
            }

            definition.MatrixGlyphs = list.Count > 0 ? list.ToArray() : null;
        }

        foreach (var (path, token) in theme.Tokens)
        {
            if (path.StartsWith("icons.", StringComparison.Ordinal) && token.Value.Kind == ThemeValueKind.Text && token.Value.Text is { } iconPath)
            {
                definition.Icons[path["icons.".Length..]] = iconPath;
            }
        }

        return definition;
    }

    private static ThemeRawEntry Entry(JsonNode node, string jsonPath) => new(node, jsonPath);
}
