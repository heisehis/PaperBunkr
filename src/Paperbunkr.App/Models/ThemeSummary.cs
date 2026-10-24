using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row in the Preferences Appearance tab's theme grid — key, display name, whether it's the
/// currently active theme, and a handful of the theme's real theme.json colors (docs/superpowers/
/// specs/2026-09-07-appearance-redesign-design.md) driving its mini UI-mockup preview card.
/// Renamed from <c>SkinSummary</c> by docs/superpowers/specs/2026-09-16-theme-system-design.md.
/// </summary>
public class ThemeSummary
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    /// <summary>"Light" or "Dark" - <see cref="Models.ThemeDefinition.Mode"/> passed straight through, so the Appearance screen can gate the true-black toggle's visibility without a second service call.</summary>
    public string Mode { get; init; } = "Dark";

    /// <summary>Preview card background — the skin's Bg color.</summary>
    public required IBrush BackgroundBrush { get; init; }

    /// <summary>Preview card's mini title-bar — the skin's Chrome color.</summary>
    public required IBrush ChromeBrush { get; init; }

    /// <summary>Preview card's title-bar dot — the skin's Accent color.</summary>
    public required IBrush AccentBrush { get; init; }

    /// <summary>Preview card's mini sidebar — the skin's Surface3 color.</summary>
    public required IBrush SurfaceBrush { get; init; }

    /// <summary>Preview card's mini content lines — the skin's TextMuted color.</summary>
    public required IBrush TextMutedBrush { get; init; }

    // ===================== Inspector data (docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md) =====================

    /// <summary>"Dark", "Light" or "Accessibility" - the gallery group this theme sits under (see <c>ThemeService.GroupOf</c>).</summary>
    public string Group { get; init; } = "Dark";

    public IBrush? ChromeSurfaceBrush { get; init; }

    public IBrush? Surface1Brush { get; init; }

    public IBrush? BorderBrush { get; init; }

    public IBrush? TextBrush { get; init; }

    public IBrush? AccentTextBrush { get; init; }

    public IBrush? BadgeBrush { get; init; }

    public IBrush? SuccessBrush { get; init; }

    public IBrush? ChartBlueBrush { get; init; }

    public IBrush? ChartVioletBrush { get; init; }

    /// <summary>WCAG contrast of the theme's text colour on its background.</summary>
    public double TextContrast { get; init; }

    /// <summary>WCAG contrast of the theme's accent colour on its background (the accent is used as literal text colour in places, so it is held to the 4.5:1 text bar).</summary>
    public double AccentContrast { get; init; }

    public string TextContrastText => FormatContrast(TextContrast);

    public string AccentContrastText => FormatContrast(AccentContrast);

    public bool TextContrastOk => TextContrast >= 4.5;

    public bool AccentContrastOk => AccentContrast >= 4.5;

    public string ModeLabel => string.Equals(Mode, "Light", System.StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";

    public string Subtitle => $"{ModeLabel} theme";

    public string TextContrastAutomationName => $"Text contrast {TextContrastText.Replace(":1", " to 1")}, {(TextContrastOk ? "passes" : "below the 4.5 to 1 target")}";

    public string AccentContrastAutomationName => $"Accent contrast {AccentContrastText.Replace(":1", " to 1")}, {(AccentContrastOk ? "passes" : "below the 4.5 to 1 target")}";

    // ===================== Family / variant (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 2) =====================

    /// <summary>The install key of the family this entry belongs to (equal to <see cref="Key"/> for a single-variant family).</summary>
    public string FamilyKey { get; init; } = string.Empty;

    /// <summary>The family's stable identity - what user overrides are stored under.</summary>
    public string FamilyId { get; init; } = string.Empty;

    public string FamilyName { get; init; } = string.Empty;

    public string VariantId { get; init; } = Paperbunkr.App.Theming.ThemeDocument.DefaultVariantId;

    public string VariantName { get; init; } = string.Empty;

    /// <summary>How many variants the family has; 1 for every legacy theme.</summary>
    public int VariantCount { get; init; } = 1;

    public Paperbunkr.App.Theming.ThemeVariantKind VariantKind { get; init; }

    public bool HasVariants => VariantCount > 1;

    /// <summary>True for a theme.json with no schemaVersion (the original format), adapted in memory.</summary>
    public bool IsLegacy { get; init; }

    public bool IsBuiltIn { get; init; }

    /// <summary>Report-only findings for this variant as authored (contrast pairs below target). Never used to change the theme.</summary>
    public System.Collections.Generic.IReadOnlyList<string> Warnings { get; init; } = System.Array.Empty<string>();

    public bool HasWarnings => Warnings.Count > 0;

    public string WarningSummary => Warnings.Count == 1 ? "1 contrast note" : $"{Warnings.Count} contrast notes";

    private static string FormatContrast(double ratio) => ratio.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":1";
}

/// <summary>One captioned group in the theme gallery (Dark / Light / Accessibility).</summary>
public sealed record ThemeGroup(string Title, System.Collections.Generic.IReadOnlyList<ThemeSummary> Themes);
