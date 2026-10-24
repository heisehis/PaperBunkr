using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace Paperbunkr.App.Theming;

/// <summary>How a resolved token is written into the live application resources (the legacy <c>Pb*</c> bridge, design doc section 6.5).</summary>
public enum ThemeResourceForm
{
    /// <summary>Not written anywhere - consumed by the engine only.</summary>
    None,

    /// <summary><c>Pb{Key}Color</c> and <c>Pb{Key}Brush</c>.</summary>
    ColorAndBrush,

    /// <summary><c>Pb{Key}Color</c> only (the hero gradient stops and scrim are consumed as colours inside gradients).</summary>
    ColorOnly,

    /// <summary><c>Pb{Key}</c> as a double.</summary>
    Number,

    /// <summary><c>Pb{Key}</c> as a uniform <c>CornerRadius</c>.</summary>
    CornerRadius,
}

/// <summary>Lets a derivation rule read other tokens without knowing how the resolver stores them.</summary>
public interface IThemeDerivationContext
{
    bool IsLight { get; }

    /// <summary>The resolved colour of another role, or null when it cannot be resolved (a cycle, or genuinely nothing to go on).</summary>
    Color? Color(string path);

    /// <summary>The first palette entry among <paramref name="names"/> that the theme defines as a colour.</summary>
    Color? Palette(params string[] names);
}

/// <summary>
/// One known token: its type, where it lands in the legacy resources, and how to compute it when a theme does not set it.
/// </summary>
public sealed record ThemeRole(
    string Path,
    ThemeValueKind Kind,
    ThemeResourceForm Form = ThemeResourceForm.None,
    string? ResourceKey = null,
    Func<IThemeDerivationContext, ThemeValue?>? Derive = null,
    string? Description = null);

/// <summary>
/// The engine's published token vocabulary (design doc section 6.1): the semantic roles, the metrics and the settings a theme can set, each with
/// a deterministic derivation so a small theme stays valid. The first block mirrors the legacy <c>theme.json</c> colour fields one to one,
/// which is what keeps every existing theme byte-identical through the bridge; the second block is new in v2.
/// </summary>
public static class ThemeVocabulary
{
    // Text placed on a filled accent / badge: a warm near-black or a warm near-white, whichever reads better.
    private static readonly Color InkDark = Color.Parse("#241505");
    private static readonly Color InkLight = Color.Parse("#FFF8EC");

    /// <summary>What a role falls back to when the theme gives nothing to derive from: the <c>default</c> theme for dark, <c>daylight</c> for light.</summary>
    private static readonly Dictionary<string, (string Dark, string Light)> ModeDefaults = new(StringComparer.Ordinal)
    {
        ["surface.canvas"] = ("#0A0B0D", "#F7F3EC"),
        ["text.primary"] = ("#ECE7DB", "#2A2620"),
        ["action.primary"] = ("#C9803F", "#985A27"),
        ["status.success"] = ("#5FA889", "#3E7A56"),
        ["status.danger"] = ("#D96C6C", "#D96C6C"),
        ["chart.series.1"] = ("#5B8DBE", "#5B8DBE"),
        ["chart.series.2"] = ("#9B7EBD", "#9B7EBD"),
        ["era.platinum"] = ("#9AA5B1", "#5F6B78"),
        ["era.golden"] = ("#D4A437", "#9A7310"),
        ["era.silver"] = ("#B8C0CC", "#6E7886"),
        ["era.bronze"] = ("#C07A45", "#96542A"),
    };

    private static ThemeValue? Default(IThemeDerivationContext c, string path) =>
        ModeDefaults.TryGetValue(path, out var pair) ? ThemeValue.Of(Color.Parse(c.IsLight ? pair.Light : pair.Dark)) : null;

    private static ThemeValue? Of(Color? color) => color is { } value ? ThemeValue.Of(value) : null;

    private static ThemeRole Legacy(string path, string resourceKey, Func<IThemeDerivationContext, ThemeValue?> derive, string description, ThemeResourceForm form = ThemeResourceForm.ColorAndBrush) =>
        new(path, ThemeValueKind.Color, form, resourceKey, derive, description);

    public static IReadOnlyList<ThemeRole> Roles { get; } = BuildRoles();

    private static readonly Dictionary<string, ThemeRole> ByPath = Roles.ToDictionary(r => r.Path, StringComparer.Ordinal);

    public static ThemeRole? Find(string path) => ByPath.TryGetValue(path, out var role) ? role : null;

    /// <summary>
    /// The roles that depend on the accent. When a user layer replaces <c>action.primary</c>, a theme's own authored value for these is stale
    /// (it was tuned for the theme's accent), so they are re-derived - exactly what the pre-v2 accent override did.
    /// </summary>
    public static IReadOnlyList<string> AccentDependents { get; } = new[] { "text.accent", "action.soft", "glow.color" };

    /// <summary>The surfaces the legacy True Black option forces to pure black.</summary>
    public static IReadOnlyList<string> TrueBlackSurfaces { get; } = new[]
    {
        "surface.canvas", "surface.navigation", "surface.recessed", "surface.default", "surface.raised", "surface.overlay",
    };

    /// <summary>Text / background pairs checked for contrast (design doc section 13.1). Report-only.</summary>
    public static IReadOnlyList<(string Foreground, string Background, double Target, string Label)> ContrastPairs { get; } = new[]
    {
        ("text.primary", "surface.canvas", 4.5, "Text"),
        ("text.secondary", "surface.canvas", 4.5, "Secondary text"),
        ("text.muted", "surface.canvas", 3.0, "Muted text"),
        ("action.primary", "surface.canvas", 4.5, "Accent"),
        ("text.accent", "surface.canvas", 4.5, "Accent text"),
        ("text.primary", "surface.raised", 4.5, "Text on cards"),
        ("badge.text", "badge.background", 4.5, "Badge text"),
        ("status.success", "surface.canvas", 3.0, "Success"),
        ("status.danger", "surface.canvas", 3.0, "Danger"),
        ("border.default", "surface.canvas", 1.2, "Borders"),
    };

    private static List<ThemeRole> BuildRoles()
    {
        var roles = new List<ThemeRole>
        {
            // ===== Legacy block: one role per theme.json colour, same resource keys the app has always used. =====
            Legacy("surface.canvas", "Bg", c => Of(c.Palette("canvas", "background", "bg")) ?? Default(c, "surface.canvas"), "The app background."),
            Legacy("surface.navigation", "Chrome", c => Of(Blend(c, "surface.canvas", "text.primary", 0.045)), "Navigation rail, title bar and other chrome."),
            Legacy("border.default", "Border", c => Of(Blend(c, "surface.canvas", "text.primary", 0.16)), "Hairlines and control outlines."),
            Legacy("text.primary", "Text", c => Of(c.Palette("ink", "text", "foreground")) ?? Default(c, "text.primary"), "Body text."),
            Legacy("text.secondary", "TextMuted", c => Of(Blend(c, "text.primary", "surface.canvas", 0.28)), "Secondary text."),
            Legacy("text.muted", "TextFaint", DeriveMutedText, "Captions and hints."),
            Legacy("action.primary", "Accent", c => Of(c.Palette("accent", "primary")) ?? Default(c, "action.primary"), "The accent: primary buttons, selection, links."),
            Legacy("text.accent", "AccentText", c => Adjust(c, "action.primary", "surface.canvas", 4.5), "The accent as a text colour, held to 4.5:1 on the background."),
            Legacy("action.soft", "AccentSoft", c => Alpha(c, "action.primary", 0x29), "A translucent accent wash."),
            Legacy("badge.background", "Badge", c => Of(c.Palette("badge", "highlight")) ?? Of(c.Color("action.primary")), "Badge fill."),
            Legacy("badge.text", "BadgeText", c => OnFill(c, "badge.background"), "Text on a badge."),
            Legacy("status.success", "Success", c => Of(c.Palette("success", "positive")) ?? Default(c, "status.success"), "Success, connected, done."),
            Legacy("chart.series.1", "ChartBlue", c => Of(c.Palette("chart1", "chartBlue")) ?? Default(c, "chart.series.1"), "First categorical chart colour."),
            Legacy("chart.series.2", "ChartViolet", c => Of(c.Palette("chart2", "chartViolet")) ?? Default(c, "chart.series.2"), "Second categorical chart colour."),
            Legacy("surface.recessed", "Surface0", DeriveRecessed, "The deepest surface (wells, the hero vignette)."),
            Legacy("surface.default", "Surface1", c => Of(c.Color("surface.canvas")), "Panels and toolbars."),
            Legacy("surface.raised", "Surface2", c => Of(c.Color("surface.navigation")), "Cards on a panel."),
            Legacy("surface.overlay", "Surface3", c => Of(Blend(c, "surface.canvas", "text.primary", 0.09)), "Popovers, dialogs, floating panels."),
            Legacy("glow.color", "Glow", c => Alpha(c, "text.accent", 0x66), "Hover and focus glow."),
            Legacy("hero.gradient.start", "HeroGradientStart", DeriveHeroStart, "Hero vignette, transparent stop.", ThemeResourceForm.ColorOnly),
            Legacy("hero.gradient.end", "HeroGradientEnd", DeriveHeroEnd, "Hero vignette, opaque stop.", ThemeResourceForm.ColorOnly),
            Legacy("hero.scrim", "HeroScrim", c => Of(Color.Parse(c.IsLight ? "#1F000000" : "#59000000")), "The Home hero's muting scrim.", ThemeResourceForm.ColorOnly),

            Legacy("era.platinum", "EraPlatinum", c => Default(c, "era.platinum"), "Continuity timeline: Platinum Age."),
            Legacy("era.golden", "EraGolden", c => Default(c, "era.golden"), "Continuity timeline: Golden Age."),
            Legacy("era.silver", "EraSilver", c => Default(c, "era.silver"), "Continuity timeline: Silver Age."),
            Legacy("era.bronze", "EraBronze", c => Default(c, "era.bronze"), "Continuity timeline: Bronze Age."),
            Legacy("era.modern", "EraModern", c => Of(c.Color("action.primary")), "Continuity timeline: Modern Age (follows the accent)."),

            // The ~18%-alpha tint of each era, used behind timeline bands.
            new("era.platinum.soft", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "EraPlatinumSoft", c => Alpha(c, "era.platinum", 0x2E)),
            new("era.golden.soft", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "EraGoldenSoft", c => Alpha(c, "era.golden", 0x2E)),
            new("era.silver.soft", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "EraSilverSoft", c => Alpha(c, "era.silver", 0x2E)),
            new("era.bronze.soft", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "EraBronzeSoft", c => Alpha(c, "era.bronze", 0x2E)),
            new("era.modern.soft", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "EraModernSoft", c => Alpha(c, "era.modern", 0x2E)),

            // ===== New in v2: status, state and reader roles. These were fixed app-wide colours (or did not exist) before. =====
            new("status.danger", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "Danger", DeriveDanger, "Errors and destructive actions."),
            new("status.warning", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "Warning", c => Of(c.Palette("warning", "caution")) ?? Of(c.Color("badge.background")), "Warnings and things needing attention."),
            new("status.info", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "Info", c => Of(c.Palette("info")) ?? Of(c.Color("chart.series.1")), "Neutral information."),
            new("status.success.soft", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "SuccessSoft", c => Alpha(c, "status.success", 0x2E)),
            new("status.danger.soft", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "DangerSoft", c => Alpha(c, "status.danger", 0x2E)),
            new("status.warning.soft", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "WarningSoft", c => Alpha(c, "status.warning", 0x2E)),
            new("status.info.soft", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "InfoSoft", c => Alpha(c, "status.info", 0x2E)),
            new("badge.soft", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "BadgeSoft", c => Alpha(c, "badge.background", 0x2E)),

            new("text.inverse", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "TextInverse", c => Of(c.Color("surface.canvas")), "Text on a surface filled with the text colour."),
            new("border.strong", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "BorderStrong", c => Of(Blend(c, "border.default", "text.primary", 0.35))),
            new("border.focus", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "Focus", c => Of(c.Color("text.accent"))),
            new("action.primaryText", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "OnAccent", c => OnFill(c, "action.primary"), "Text on an accent-filled control."),
            new("action.hover", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "AccentHover", c => Shift(c, "action.primary", 0.10)),
            new("action.pressed", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "AccentPressed", c => Shift(c, "action.primary", -0.12)),
            new("action.disabled", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "AccentDisabled", c => Alpha(c, "action.primary", 0x66)),
            new("selection.background", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "Selection", c => Of(c.Color("action.soft"))),
            new("selection.foreground", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "SelectionText", c => Of(c.Color("text.primary"))),
            new("focus.ring", ThemeValueKind.Color, ThemeResourceForm.None, null, c => Alpha(c, "glow.color", 0x99)),

            new("chart.series.3", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "ChartSeries3", c => Of(c.Color("status.success"))),
            new("chart.series.4", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "ChartSeries4", c => Of(c.Color("badge.background"))),
            new("chart.series.5", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "ChartSeries5", c => Of(c.Color("action.primary"))),
            new("chart.series.6", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "ChartSeries6", c => Of(c.Color("status.danger"))),

            // Reader chrome follows the app theme; book and comic CONTENT keep their own choices (design doc section 11).
            new("reader.chrome.background", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "ReaderChrome", c => Of(c.Color("surface.raised"))),
            new("reader.chrome.border", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "ReaderBorder", c => Of(c.Color("border.default"))),
            new("reader.chrome.text", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "ReaderText", c => Of(c.Color("text.primary"))),
            new("reader.chrome.textMuted", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "ReaderTextMuted", c => Of(c.Color("text.secondary"))),
            new("reader.background.page", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "ReaderPage", c => Of(c.Color("surface.default")), "What 'Match app' gives a book page as its background."),
            new("reader.background.text", ThemeValueKind.Color, ThemeResourceForm.ColorAndBrush, "ReaderPageText", c => Of(c.Color("text.primary")), "What 'Match app' gives a book page as its text colour."),

            // ===== Metrics =====
            new("metrics.spacingUnit", ThemeValueKind.Number, ThemeResourceForm.Number, "SpacingUnit", _ => ThemeValue.Of(4)),
            new("metrics.radius", ThemeValueKind.Number, ThemeResourceForm.CornerRadius, "Radius", _ => ThemeValue.Of(7)),
            new("metrics.radiusSm", ThemeValueKind.Number, ThemeResourceForm.CornerRadius, "RadiusSm", _ => ThemeValue.Of(5)),
            new("metrics.radiusLg", ThemeValueKind.Number, ThemeResourceForm.CornerRadius, "RadiusLg", _ => ThemeValue.Of(14)),

            // Optional: written only when a theme sets them, so a theme without them leaves the framework's own scrollbar alone.
            new("metrics.scrollbarWidth", ThemeValueKind.Number, ThemeResourceForm.Number, "ScrollbarWidth"),
            new("metrics.scrollbarThumbOpacity", ThemeValueKind.Number, ThemeResourceForm.Number, "ScrollbarThumbOpacity"),

            // ===== Settings (engine-consumed, no resource) =====
            new("settings.windowBackdrop", ThemeValueKind.Text, Derive: _ => ThemeValue.Of("None")),
            new("settings.matrixGlyphs", ThemeValueKind.Json),
        };

        return roles;
    }

    private static Color? Blend(IThemeDerivationContext c, string from, string to, double amount) =>
        c.Color(from) is { } a && c.Color(to) is { } b ? ThemeColorMath.Opaque(ThemeColorMath.Mix(a, b, amount)) : null;

    private static ThemeValue? Alpha(IThemeDerivationContext c, string path, byte alpha) =>
        c.Color(path) is { } color ? ThemeValue.Of(ThemeColorMath.WithAlpha(color, alpha)) : null;

    private static ThemeValue? Adjust(IThemeDerivationContext c, string foreground, string background, double target) =>
        c.Color(foreground) is { } fg && c.Color(background) is { } bg ? ThemeValue.Of(ThemeColorMath.AdjustForContrast(fg, bg, target)) : null;

    private static ThemeValue? OnFill(IThemeDerivationContext c, string fill) =>
        c.Color(fill) is { } background ? ThemeValue.Of(ThemeColorMath.ReadableOn(background, InkDark, InkLight)) : null;

    /// <summary>Hover lightens on a dark theme and darkens on a light one; pressed (a negative amount) goes the other way.</summary>
    private static ThemeValue? Shift(IThemeDerivationContext c, string path, double amount)
    {
        if (c.Color(path) is not { } color)
        {
            return null;
        }

        bool lighten = c.IsLight ? amount < 0 : amount > 0;
        double magnitude = Math.Abs(amount);
        return ThemeValue.Of(lighten ? ThemeColorMath.Lighten(color, magnitude) : ThemeColorMath.Darken(color, magnitude));
    }

    private static ThemeValue? DeriveMutedText(IThemeDerivationContext c)
    {
        if (Blend(c, "text.primary", "surface.canvas", 0.52) is not { } muted || c.Color("surface.canvas") is not { } canvas)
        {
            return null;
        }

        return ThemeValue.Of(ThemeColorMath.AdjustForContrast(muted, canvas, 3.0));
    }

    private static ThemeValue? DeriveRecessed(IThemeDerivationContext c)
    {
        if (c.Color("surface.canvas") is not { } canvas)
        {
            return null;
        }

        return c.IsLight
            ? Of(Blend(c, "surface.canvas", "text.primary", 0.07))
            : ThemeValue.Of(ThemeColorMath.Opaque(ThemeColorMath.Darken(canvas, 0.6)));
    }

    private static ThemeValue? DeriveHeroStart(IThemeDerivationContext c) =>
        c.IsLight ? Alpha(c, "surface.canvas", 0x00) : ThemeValue.Of(Color.Parse("#00000000"));

    private static ThemeValue? DeriveHeroEnd(IThemeDerivationContext c) =>
        c.IsLight ? Of(c.Color("surface.canvas")) : Of(c.Color("surface.recessed"));

    /// <summary>The app's red, deepened on a light background until it reads (it was one fixed colour for every theme before v2).</summary>
    private static ThemeValue? DeriveDanger(IThemeDerivationContext c)
    {
        if (c.Palette("danger", "error", "negative") is { } authored)
        {
            return ThemeValue.Of(authored);
        }

        var red = Color.Parse("#D96C6C");
        return c.Color("surface.canvas") is { } canvas && c.IsLight ? ThemeValue.Of(ThemeColorMath.AdjustForContrast(red, canvas, 4.5)) : ThemeValue.Of(red);
    }
}
