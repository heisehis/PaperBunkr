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
    public static IBrush ContentBackground(BookTheme theme) => theme switch
    {
        BookTheme.Light => Brushes.White,
        BookTheme.Dark => new SolidColorBrush(Color.Parse("#1C1C1C")),
        BookTheme.Sepia => new SolidColorBrush(Color.Parse("#F2E6CE")),
        BookTheme.OledBlack => Brushes.Black,
        BookTheme.HighContrast => Brushes.Black,
        _ => new SolidColorBrush(Color.Parse("#14161B")), // MatchAppSkin
    };

    public static IBrush ContentForeground(BookTheme theme) => theme switch
    {
        BookTheme.Light => new SolidColorBrush(Color.Parse("#1A1A1A")),
        BookTheme.Dark => new SolidColorBrush(Color.Parse("#E0E0E0")),
        BookTheme.Sepia => new SolidColorBrush(Color.Parse("#3A2F22")),
        BookTheme.OledBlack => new SolidColorBrush(Color.Parse("#E0E0E0")),
        BookTheme.HighContrast => Brushes.White,
        _ => new SolidColorBrush(Color.Parse("#ECE7DB")), // MatchAppSkin
    };

    /// <summary>
    /// Chrome bar / settings sheet background - the same per-theme hue as <see cref="ContentBackground"/>
    /// at ~80% opacity (0xCC) so it reads as an overlay atop the reading pane rather than a fully
    /// opaque bar. MatchAppSkin's value is byte-identical to the pre-redesign chrome bars' hardcoded
    /// <c>#CC14161B</c>, so that theme's look is unchanged by this redesign.
    /// </summary>
    public static IBrush ChromeBackground(BookTheme theme) => theme switch
    {
        BookTheme.Light => new SolidColorBrush(Color.Parse("#CCFFFFFF")),
        BookTheme.Dark => new SolidColorBrush(Color.Parse("#CC1C1C1C")),
        BookTheme.Sepia => new SolidColorBrush(Color.Parse("#CCF2E6CE")),
        BookTheme.OledBlack => new SolidColorBrush(Color.Parse("#CC000000")),
        BookTheme.HighContrast => new SolidColorBrush(Color.Parse("#CC000000")),
        _ => new SolidColorBrush(Color.Parse("#CC14161B")), // MatchAppSkin
    };

    /// <summary>
    /// Chrome icon/text color - same as <see cref="ContentForeground"/>. A theme's own text color
    /// already has a deliberate contrast pairing with its background; reusing it for the chrome
    /// keeps icons legible against <see cref="ChromeBackground"/> without a second contrast table to
    /// maintain in parallel.
    /// </summary>
    public static IBrush ChromeForeground(BookTheme theme) => ContentForeground(theme);
}
