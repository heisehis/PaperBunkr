using System.Collections.Generic;

namespace Paperbunkr.App.Models;

/// <summary>
/// Deserialized <c>theme.json</c> (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §2, renamed from <c>SkinTheme</c> by docs/superpowers/specs/2026-09-16-theme-system-design.md) -
/// field names map 1:1 onto the existing <c>Pb*</c> token suffixes in App.axaml (e.g.
/// <see cref="SkinColors.Bg"/> → <c>PbBgColor</c>/<c>PbBgBrush</c>).
/// </summary>
public class ThemeDefinition
{
    public string Name { get; set; } = "Unnamed";

    /// <summary>
    /// "Light" or "Dark" (docs/superpowers/specs/2026-09-16-theme-system-design.md § Data & schema) -
    /// drives <see cref="Avalonia.Application.RequestedThemeVariant"/> in <c>ThemeService.ApplyTheme</c>.
    /// A plain string, not a C# enum, matching every other field in this schema (no JSON enum
    /// converter precedent anywhere else in <c>theme.json</c>) - compared case-insensitively.
    /// Defaults to "Dark", matching 4 of the 5 pre-existing built-in themes, so an older/third-party
    /// theme.json that predates this field still loads with sane behavior instead of throwing.
    /// </summary>
    public string Mode { get; set; } = "Dark";

    /// <summary>
    /// Free-form, backend-only grouping tag (e.g. "matrix") - not read by any UI in this phase, exists
    /// so a future pass can filter/group themes without another schema migration. Null for every
    /// theme that doesn't need one.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Suggested font family for this theme (docs/superpowers/specs/2026-09-16-theme-system-design.md
    /// § Extended scope - "Per-theme default font"), null = no suggestion. Precedence in
    /// <c>ThemeService.ApplyFontResource</c>: <c>AppSettings.SelectedFontFamily</c> (explicit global
    /// override) wins if set; else this; else the existing hardcoded default. Matrix is the only
    /// built-in theme that sets it so far.
    /// </summary>
    public string? DefaultFontFamily { get; set; }

    /// <summary>
    /// "None"/"Mica"/"Acrylic" (docs/superpowers/specs/2026-09-16-theme-system-design.md § Extended
    /// scope - "Mica/Acrylic backdrop for windows_11"), Windows-only, ignored elsewhere, default
    /// "None". Drives <c>MainWindow.TransparencyLevelHint</c> via <c>ThemeService.WindowBackdropRequested</c>
    /// - not wired to a ControlTheme/scrollbar-style mechanism like <see cref="ScrollbarWidth"/>
    /// below, since <c>TransparencyLevelHint</c> already has real precedent in this codebase
    /// (<c>OverlayHostWindow.cs</c>, <c>SplashWindow.axaml</c>).
    /// </summary>
    public string WindowBackdrop { get; set; } = "None";

    /// <summary>
    /// Optional scrollbar geometry override (§ Extended scope - "Scrollbar geometry tokens"). Schema
    /// only in this pass - exposed as PbScrollbarWidth/PbScrollbarThumbOpacity resources by
    /// <c>ThemeService</c>, but deliberately NOT consumed by a ControlTheme override on
    /// FluentAvalonia's ScrollBar yet: rewriting that lookless control's template without being able
    /// to visually verify the result carries real app-wide breakage risk for a purely cosmetic,
    /// explicitly-optional feature - left for a follow-up pass that can actually look at the result
    /// on screen. Null = FluentAvalonia's own default, unaffected.
    /// </summary>
    public double? ScrollbarWidth { get; set; }

    /// <summary>See <see cref="ScrollbarWidth"/> - same "schema + resource only, no ControlTheme yet" scope cut.</summary>
    public double? ScrollbarThumbOpacity { get; set; }

    /// <summary>
    /// Character set <c>MatrixRainOverlay</c> draws from, null/empty = fall back to the overlay's
    /// own hardcoded default katakana set. Only meaningful for the Matrix theme, but any theme could
    /// set it if a future novelty theme wants its own rain glyphs.
    /// </summary>
    public string[]? MatrixGlyphs { get; set; }

    public SkinColors Colors { get; set; } = new();

    public double SpacingUnit { get; set; } = 4;

    /// <summary>The "Md" radius tier - unchanged key/meaning from before the elevation-scale expansion (docs/superpowers/specs/2026-08-24-design-language-foundation-design.md).</summary>
    public double Radius { get; set; } = 7;

    /// <summary>Smaller radius tier (chips, small buttons). Additive - missing in an older/third-party theme.json falls back to this default.</summary>
    public double RadiusSm { get; set; } = 5;

    /// <summary>Larger radius tier (floating panels, large cards). Additive - missing in an older/third-party theme.json falls back to this default.</summary>
    public double RadiusLg { get; set; } = 14;

    /// <summary>Icon key -> relative path within the skin (e.g. "icons/library.png"). Not consumed by any UI yet - see design spec §2.</summary>
    public Dictionary<string, string> Icons { get; set; } = new();
}

public class SkinColors
{
    // Darkened per direct user feedback during Phase 3 brainstorming - "really dark, if not
    // black." Bg/Chrome stay value-twins of Surface1/Surface2 (same invariant Phase 1 set up),
    // just shifted toward true black.
    public string Bg { get; set; } = "#0A0B0D";
    public string Chrome { get; set; } = "#131519";
    public string Border { get; set; } = "#2A2E37";
    public string Text { get; set; } = "#ECE7DB";
    public string TextMuted { get; set; } = "#B3ADA0";
    public string TextFaint { get; set; } = "#77726A";
    public string Accent { get; set; } = "#C9803F";
    public string AccentText { get; set; } = "#E0995A";
    public string AccentSoft { get; set; } = "#29C9803F";
    public string Badge { get; set; } = "#D7AC4C";
    public string BadgeText { get; set; } = "#241505";
    public string Success { get; set; } = "#5FA889";

    // Added by docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md §8 - categorical
    // chart colors for the Stats screen's multi-segment donuts (7-value ReadingStatus, 5-value
    // ContentType) where the four names above aren't enough distinct hues on their own. Additive,
    // same "a theme.json predating this simply omits the key and gets the default" rule as the
    // elevation-scale keys below.
    public string ChartBlue { get; set; } = "#5B8DBE";
    public string ChartViolet { get; set; } = "#9B7EBD";

    // Elevation scale + glow/hero-gradient tokens added by docs/superpowers/specs/2026-08-24-
    // design-language-foundation-design.md. Additive - a theme.json predating this change simply
    // omits these keys and gets the defaults below (matching the "default" skin's own values, the
    // most reasonable fallback for a skin authored against the old flat Bg/Chrome scheme).

    /// <summary>App background - literal true black, per direct user feedback during Phase 3 ("really dark, if not black"). Distinct from <see cref="Bg"/>, which means "surface1" - see the design doc's Color System section for the tier mapping.</summary>
    public string Surface0 { get; set; } = "#000000";

    /// <summary>Panels/toolbars - same role <see cref="Bg"/> played before the elevation scale.</summary>
    public string Surface1 { get; set; } = "#0A0B0D";

    /// <summary>Cards-on-surface1 (e.g. poster tiles) - same role <see cref="Chrome"/> played before the elevation scale.</summary>
    public string Surface2 { get; set; } = "#131519";

    /// <summary>Popovers/modals/floating panels - the newest, lightest tier.</summary>
    public string Surface3 { get; set; } = "#1B1E24";

    /// <summary>Amber glow used for the poster-tile/floating-panel hover+keyboard-focus ring (higher opacity than <see cref="AccentSoft"/>).</summary>
    public string Glow { get; set; } = "#66E0995A";

    /// <summary>Hero-art vignette gradient start stop (transparent, at the art) - see <see cref="HeroGradientEnd"/>.</summary>
    public string HeroGradientStart { get; set; } = "#00000000";

    /// <summary>Hero-art vignette gradient end stop - opaque <see cref="Surface0"/>, not a tinted color, per the design doc's "dark vignette" direction.</summary>
    public string HeroGradientEnd { get; set; } = "#FF000000";
}
