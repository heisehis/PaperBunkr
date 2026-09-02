namespace Paperbunkr.Data.Entities;

/// <summary>
/// Reader theme choice (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §5;
/// <see cref="OledBlack"/>/<see cref="HighContrast"/> added by docs/superpowers/specs/
/// 2026-09-01-books-reader-ergonomics-and-annotations-design.md). See
/// <see cref="BookFontFamilyOption"/> for why this lives in <c>Paperbunkr.Data.Entities</c> rather
/// than <c>Paperbunkr.App.Models</c>.
/// </summary>
public enum BookTheme
{
    Light,
    Dark,
    Sepia,
    MatchAppSkin,

    /// <summary>True #000000 background - deeper than <see cref="Dark"/>, for OLED displays.</summary>
    OledBlack,

    /// <summary>Maximum-contrast pairing (pure black/white), independent of <see cref="Light"/>/<see cref="Dark"/>.</summary>
    HighContrast,
}
