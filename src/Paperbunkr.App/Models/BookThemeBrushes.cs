using System;
using Avalonia;
using Avalonia.Media;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Single per-<see cref="BookTheme"/> brush mapping (docs/superpowers/specs/2026-09-03-books-reader-
/// hud-redesign-design.md) - the reading content's own colors (<see cref="BookReaderSettings.Background"/>/
/// <see cref="BookReaderSettings.Foreground"/> delegate here) and the reader chrome bars'/settings
/// sheet's translucent tint (<see cref="Views.ReaderChromeTint"/>'s converters) both come from this
/// one table, so a theme always tints consistently everywhere it's used rather than two switch
/// statements that could drift apart.
/// </summary>
public static class BookThemeBrushes
{
    // What "Match app" looked like before it followed the theme, and what it still is when no theme has been applied (a headless host).
    private static readonly Color LegacyMatchAppPage = Color.Parse("#14161B");
    private static readonly Color LegacyMatchAppText = Color.Parse("#ECE7DB");

    /// <summary>
    /// The active app theme's reading surface: its <c>reader.background.page</c> and <c>reader.background.text</c> roles
    /// (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 6, decision D7), read from the live resources so
    /// "Match app" really does match whatever theme, variant and overrides are showing. Null when no theme has published them. A seam for tests.
    /// </summary>
    internal static Func<(Color Page, Color Text)?> AppColors { get; set; } = ReadAppColors;

    internal static (Color Page, Color Text)? ReadAppColors()
    {
        var resources = Application.Current?.Resources;
        if (resources is not null
            && resources.TryGetResource("PbReaderPageColor", null, out var page) && page is Color pageColor
            && resources.TryGetResource("PbReaderPageTextColor", null, out var text) && text is Color textColor)
        {
            return (pageColor, textColor);
        }

        return null;
    }

    private static Color MatchAppPage => AppColors()?.Page ?? LegacyMatchAppPage;

    private static Color MatchAppText => AppColors()?.Text ?? LegacyMatchAppText;

    public static IBrush ContentBackground(BookTheme theme) => theme switch
    {
        BookTheme.Light => Brushes.White,
        BookTheme.Dark => new SolidColorBrush(Color.Parse("#1C1C1C")),
        BookTheme.Sepia => new SolidColorBrush(Color.Parse("#F2E6CE")),
        BookTheme.OledBlack => Brushes.Black,
        BookTheme.HighContrast => Brushes.Black,
        _ => new SolidColorBrush(MatchAppPage), // MatchAppSkin
    };

    public static IBrush ContentForeground(BookTheme theme) => theme switch
    {
        BookTheme.Light => new SolidColorBrush(Color.Parse("#1A1A1A")),
        BookTheme.Dark => new SolidColorBrush(Color.Parse("#E0E0E0")),
        BookTheme.Sepia => new SolidColorBrush(Color.Parse("#3A2F22")),
        BookTheme.OledBlack => new SolidColorBrush(Color.Parse("#E0E0E0")),
        BookTheme.HighContrast => Brushes.White,
        _ => new SolidColorBrush(MatchAppText), // MatchAppSkin
    };

    /// <summary>
    /// Chrome bar / settings sheet background - the same per-theme hue as <see cref="ContentBackground"/>
    /// at ~80% opacity (0xCC) so it reads as an overlay atop the reading pane rather than a fully
    /// opaque bar. MatchAppSkin is the app theme's reading surface at the same opacity (<c>#CC14161B</c>, the
    /// pre-redesign chrome colour, only when no theme has been applied).
    /// </summary>
    public static IBrush ChromeBackground(BookTheme theme) => theme switch
    {
        BookTheme.Light => new SolidColorBrush(Color.Parse("#CCFFFFFF")),
        BookTheme.Dark => new SolidColorBrush(Color.Parse("#CC1C1C1C")),
        BookTheme.Sepia => new SolidColorBrush(Color.Parse("#CCF2E6CE")),
        BookTheme.OledBlack => new SolidColorBrush(Color.Parse("#CC000000")),
        BookTheme.HighContrast => new SolidColorBrush(Color.Parse("#CC000000")),
        _ => new SolidColorBrush(Color.FromArgb(0xCC, MatchAppPage.R, MatchAppPage.G, MatchAppPage.B)), // MatchAppSkin
    };

    /// <summary>
    /// Chrome icon/text color - same as <see cref="ContentForeground"/>. A theme's own text color
    /// already has a deliberate contrast pairing with its background; reusing it for the chrome
    /// keeps icons legible against <see cref="ChromeBackground"/> without a second contrast table to
    /// maintain in parallel.
    /// </summary>
    public static IBrush ChromeForeground(BookTheme theme) => ContentForeground(theme);
}
