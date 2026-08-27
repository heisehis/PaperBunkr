using System.Collections.Generic;

namespace Paperbunkr.App.Models;

/// <summary>
/// Deserialized <c>theme.json</c> (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §2) - field names map 1:1 onto the existing <c>Pb*</c> token suffixes in App.axaml (e.g.
/// <see cref="SkinColors.Bg"/> → <c>PbBgColor</c>/<c>PbBgBrush</c>).
/// </summary>
public class SkinTheme
{
    public string Name { get; set; } = "Unnamed";

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
