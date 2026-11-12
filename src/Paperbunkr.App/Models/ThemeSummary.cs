using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row in the Preferences Appearance tab's theme grid — key, display name, whether it's the
/// currently active theme, and a handful of the theme's real theme.json colors (docs/superpowers/
/// specs/2026-09-07-appearance-redesign-design.md) driving its mini UI-mockup preview card.
/// Renamed from <c>SkinSummary</c> by docs/superpowers/specs/2026-09-16-theme-system-design.md.
/// </summary>
public partial class ThemeSummary : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Settable so selecting a theme only flips the flag on the tiles already shown, instead of rebuilding the gallery from inside the tile's own
    /// Click (docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md; CLAUDE.md "don't remove/detach a control from inside a routed event").
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isActive;

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

    /// <summary>A theme the user installed (shown with an "Installed" mark and under the Installed filter).</summary>
    public bool IsInstalled => !IsBuiltIn;

    /// <summary>
    /// An OLED variant is reached through True black, not the gallery (design Q7), so it is left out of the tiles and the variant switch - unless it is the
    /// theme in use, so the user never loses sight of what is applied.
    /// </summary>
    public bool IsGalleryHidden => VariantKind == Paperbunkr.App.Theming.ThemeVariantKind.Oled && VariantCount > 1 && !IsActive;

    // ===================== Gallery tile (docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md Q4) =====================

    /// <summary>The theme's own canvas: its gradient or texture when it has one, else the solid background.</summary>
    public IBrush? CanvasBrush { get; init; }

    /// <summary>The theme's own navigation surface (material-aware), for the tile's title bar and sidebar.</summary>
    public IBrush? NavigationBrush { get; init; }

    /// <summary>The theme's small radius, so a square theme shows a square tile and a round one a round tile.</summary>
    public Avalonia.CornerRadius TileRadius { get; init; } = new(5);

    /// <summary>The theme's primary-button radius and text colour.</summary>
    public Avalonia.CornerRadius ButtonRadius { get; init; } = new(5);

    /// <summary>
    /// The radii for the tile's small mockup pieces (covers, button, chip): the theme's own radii scaled to the mockup's size and capped, so a round
    /// theme reads as rounded without its tiny pieces turning into ovals (design language: no oval pills), and a square theme stays square.
    /// </summary>
    public Avalonia.CornerRadius MiniRadius => Scaled(TileRadius.TopLeft, 0.4, 3);

    public Avalonia.CornerRadius MiniButtonRadius => Scaled(ButtonRadius.TopLeft, 0.4, 3);

    private static Avalonia.CornerRadius Scaled(double radius, double factor, double max) => new(System.Math.Min(radius * factor, max));

    public IBrush? ButtonForegroundBrush { get; init; }

    /// <summary>The theme's heading font, for the tile's "Aa". Falls back to the app font when the theme names none.</summary>
    public FontFamily HeadingFontFamily { get; init; } = FontFamily.Default;

    /// <summary>Report-only findings for this variant as authored (contrast pairs below target). Never used to change the theme.</summary>
    public System.Collections.Generic.IReadOnlyList<string> Warnings { get; init; } = System.Array.Empty<string>();

    public bool HasWarnings => Warnings.Count > 0;

    public string WarningSummary => Warnings.Count == 1 ? "1 contrast note" : $"{Warnings.Count} contrast notes";

    private static string FormatContrast(double ratio) => ratio.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":1";
}

/// <summary>One button of the gallery's group switch (Dark / Light / Accessibility / Installed) with how many themes it holds.</summary>
public sealed partial class ThemeGallerySegment : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public ThemeGallerySegment(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }

    public string Title { get; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(Label), nameof(AutomationName))]
    private int _count;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isSelected;

    public string Label => $"{Title} · {Count}";

    public string AutomationName => Count == 1 ? $"{Title}, 1 theme" : $"{Title}, {Count} themes";
}
