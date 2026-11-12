using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>The personalization values the theme engine folds into every apply (docs/superpowers/specs/2026-11-06-personalization-design.md).</summary>
public sealed record PersonalizationState(int FontScale, string? HeadingFont, int CardElevation, int FocusThickness, SidebarStyle SidebarStyle)
{
    public static PersonalizationState Default { get; } = new(100, null, 1, 1, SidebarStyle.Standard);

    public static PersonalizationState From(AppSettings settings) => new(
        PersonalizationMath.NormalizeFontScale(settings.FontScale),
        string.IsNullOrWhiteSpace(settings.HeadingFontFamily) ? null : settings.HeadingFontFamily.Trim(),
        PersonalizationMath.NormalizeCardElevation(settings.CardElevation),
        PersonalizationMath.NormalizeFocusThickness(settings.FocusRingThickness),
        Enum.IsDefined(settings.SidebarStyle) ? settings.SidebarStyle : SidebarStyle.Standard);
}

/// <summary>
/// The sidebar style as an observable the shell binds to (a class on the sidebar host), so switching it never removes a <c>Style</c> at runtime
/// (that leaves stale values on template parts - see the theme system v2 notes).
/// </summary>
public sealed partial class PersonalizationRuntime : ObservableObject
{
    public static PersonalizationRuntime Instance { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarPill))]
    [NotifyPropertyChangedFor(nameof(IsSidebarTint))]
    private SidebarStyle _sidebarStyle = SidebarStyle.Standard;

    public bool IsSidebarPill => SidebarStyle == SidebarStyle.Pill;

    public bool IsSidebarTint => SidebarStyle == SidebarStyle.Tint;
}

/// <summary>
/// Personalization (docs/superpowers/specs/2026-11-06-personalization-design.md): text scale, heading font, card elevation and focus-ring thickness are
/// written into the same apply transaction as the theme, so a theme switch keeps them and a default value reproduces the old resources exactly.
/// The sidebar style is not a resource; it is published through <see cref="PersonalizationRuntime"/>.
/// </summary>
public partial class ThemeService
{
    /// <summary>Process-wide, like the glow tier: tests reset it with <see cref="ResetPersonalizationForTests"/>.</summary>
    private static PersonalizationState _personal = PersonalizationState.Default;

    /// <summary>The app's own type scale (AppTokens.axaml) for the size keys a theme may leave unset.</summary>
    private static readonly (string Key, double Default)[] FontSizeKeys =
    {
        ("PbFontSizeMicro", 10), ("PbFontSizeOverline", 10), ("PbFontSizeCaption", 11), ("PbFontSizeMeta", 12),
        ("PbFontSizeBody", 13), ("PbFontSizeControl", 13), ("PbFontSizeSection", 15), ("PbFontSizeTitle", 18),
        ("PbFontSizeHeading", 22), ("PbFontSizeHero", 42),
    };

    public static PersonalizationState CurrentPersonalization => _personal;

    internal static void ResetPersonalizationForTests()
    {
        _personal = PersonalizationState.Default;
        PersonalizationRuntime.Instance.SidebarStyle = SidebarStyle.Standard;
    }

    /// <summary>Spread multiplier for the focus/hover ring: Thin 0.75, Normal 1, Thick 1.5.</summary>
    private static double FocusThicknessFactor => PersonalizationMath.FocusRingStroke(_personal.FocusThickness) / 2.0;

    private static void LoadPersonalization(AppSettings settings)
    {
        _personal = PersonalizationState.From(settings);
        PersonalizationRuntime.Instance.SidebarStyle = _personal.SidebarStyle;
    }

    /// <summary>The persisted personalization values.</summary>
    public PersonalizationState GetPersonalization()
    {
        using var context = _contextFactory();
        return PersonalizationState.From(context.GetOrCreateAppSettings());
    }

    /// <summary>Changes personalization settings, persists them and re-applies the active theme with them (live).</summary>
    public void ApplyPersonalization(Action<AppSettings> change)
    {
        using (var context = _contextFactory())
        {
            var settings = context.GetOrCreateAppSettings();
            change(settings);
            context.SaveChanges();
            LoadPersonalization(settings);
        }

        RefreshActiveTheme();
    }

    private static readonly string[] RadiusOverridePaths = { "metrics.radius", "metrics.radiusSm", "metrics.radiusLg" };

    /// <summary>
    /// The five look values in effect (docs/superpowers/specs/2026-11-06-looks-and-presets-design.md). Corners is read back from the three Global radius overrides
    /// (50 when none is set; null when they are off the slider's curve).
    /// </summary>
    public LookValues GetLook()
    {
        var state = GetPersonalization();
        return new LookValues(ReadCorners(), state.CardElevation, state.SidebarStyle, state.FocusThickness, GetGlowTier());
    }

    private int? ReadCorners()
    {
        var values = RadiusOverridePaths.Select(p => GetOverride(ThemeOverrideScope.Global, p)).ToList();
        if (values.All(v => v is null))
        {
            return PersonalizationMath.DefaultCorners;
        }

        static double Parse(string? v, double fallback) =>
            double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : fallback;

        return PersonalizationMath.RadiiToCorners(
            Parse(values[0], PersonalizationMath.DefaultRadius),
            Parse(values[1], PersonalizationMath.DefaultRadiusSm),
            Parse(values[2], PersonalizationMath.DefaultRadiusLg));
    }

    /// <summary>
    /// Applies a whole look - corners, card depth, sidebar selection, ring width and strength - with <b>one</b> theme re-apply, so the window does not flash through
    /// intermediate looks. Leaves the accent, the fonts and the text size alone. Returns the look that was in effect before (for Undo).
    /// A null <see cref="LookValues.Corners"/> leaves the radius overrides as they are.
    /// </summary>
    public LookValues ApplyLook(LookValues look)
    {
        var previous = GetLook();

        if (look.Corners is int corners)
        {
            var radii = new Dictionary<string, string?>();
            if (corners == PersonalizationMath.DefaultCorners)
            {
                foreach (string path in RadiusOverridePaths)
                {
                    radii[path] = null;
                }
            }
            else
            {
                var (r, sm, lg) = PersonalizationMath.CornersToRadii(corners);
                radii[RadiusOverridePaths[0]] = Number(r);
                radii[RadiusOverridePaths[1]] = Number(sm);
                radii[RadiusOverridePaths[2]] = Number(lg);
            }

            SetGlobalOverridesWithoutRefresh(radii);
        }

        using (var context = _contextFactory())
        {
            var settings = context.GetOrCreateAppSettings();
            settings.CardElevation = PersonalizationMath.NormalizeCardElevation(look.CardElevation);
            settings.SidebarStyle = look.Sidebar;
            settings.FocusRingThickness = PersonalizationMath.NormalizeFocusThickness(look.FocusThickness);
            settings.GlowTier = NormalizeGlowTier(look.RingStrength);
            context.SaveChanges();
            _glowTier = settings.GlowTier;
            LoadPersonalization(settings);
        }

        RefreshActiveTheme();
        NotifyOverridesChanged();
        return previous;
    }

    private static string Number(double value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Called inside the theme's apply plan: the text scale and the heading font.</summary>
    private static void ProjectPersonalization(ThemeApplyPlan plan)
    {
        int scale = _personal.FontScale;
        foreach (var (key, fallback) in FontSizeKeys)
        {
            double themeValue = plan.Resources.TryGetValue(key, out var value) && value is double size ? size : fallback;
            bool themeSetsIt = plan.Resources.ContainsKey(key);

            if (scale != 100)
            {
                plan.Removals.Remove(key);
                plan.Resources[key] = PersonalizationMath.ScaleFontSize(themeValue, scale);
            }
            else if (!themeSetsIt && key is "PbFontSizeSection" or "PbFontSizeTitle")
            {
                // Back to AppTokens' own value after a scaled apply (the theme projector never writes these two).
                plan.Removals.Add(key);
            }
        }

        if (_personal.HeadingFont is { } heading)
        {
            var family = new FontFamily(heading);
            plan.Resources["PbHeadingFontFamily"] = family;
            plan.Resources["PbDisplayFontFamily"] = family;
        }
    }

    /// <summary>
    /// The cover shadows for the card-elevation setting. Soft reproduces the literals the styles used before this setting existed; Flat drops the depth
    /// (hover keeps its accent edge); Lifted deepens it. Hover edges now follow the theme's glow colour instead of a fixed orange.
    /// </summary>
    internal static IReadOnlyDictionary<string, BoxShadows> BuildCardShadows(Color glow, int elevation)
    {
        var edge2 = new BoxShadow { Spread = 2, Color = Color.FromArgb(0x66, glow.R, glow.G, glow.B) };
        var edge4 = new BoxShadow { Spread = 4, Color = Color.FromArgb(0x66, glow.R, glow.G, glow.B) };

        BoxShadow? Drop(double y, double blur, double spread, byte alpha) => PersonalizationMath.NormalizeCardElevation(elevation) switch
        {
            0 => null,
            2 => new BoxShadow { OffsetY = y * 1.6, Blur = blur * 1.5, Spread = spread, Color = Color.FromArgb((byte)Math.Min(255, alpha + 0x30), 0, 0, 0) },
            _ => new BoxShadow { OffsetY = y, Blur = blur, Spread = spread, Color = Color.FromArgb(alpha, 0, 0, 0) },
        };

        static BoxShadows Of(BoxShadow? drop, BoxShadow? edge = null) => (drop, edge) switch
        {
            (null, null) => default,
            (null, { } e) => new BoxShadows(e),
            ({ } d, null) => new BoxShadows(d),
            ({ } d, { } e) => new BoxShadows(d, new[] { e }),
        };

        var poster = Drop(6, 18, -4, 0x80);
        var shelf = Drop(14, 34, 0, 0x66);
        var cover = Drop(8, 24, -12, 0x80);
        return new Dictionary<string, BoxShadows>
        {
            ["PbPosterShadow"] = Of(poster),
            ["PbPosterShadowHover"] = Of(poster, edge2),
            ["PbShelfShadow"] = Of(shelf),
            ["PbShelfShadowHover"] = Of(shelf, edge4),
            ["PbCoverShadow"] = Of(cover),
        };
    }

    private static void AddCardShadowsToPlan(Color glow, ThemeApplyPlan plan)
    {
        foreach (var (key, shadows) in BuildCardShadows(glow, _personal.CardElevation))
        {
            plan.Resources[key] = shadows;
        }
    }
}
