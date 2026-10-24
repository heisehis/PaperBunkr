using System;
using Avalonia.Media;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services.Splash;

/// <summary>
/// The colours the startup splash draws with (docs/superpowers/specs/2026-10-22-splash-theme-redesign-design.md). The splash shows before the
/// database is ready, so it cannot use the live theme: it gets one of these from <see cref="SplashThemeStore"/>'s saved snapshot instead.
/// </summary>
public sealed record SplashPalette(
    bool IsLight,
    Color Surface,
    Color Text,
    Color TextMuted,
    Color TextFaint,
    Color Border,
    Color Accent,
    Color AccentText,
    Color AccentSoft,
    double CornerRadius)
{
    /// <summary>The built-in "default" theme (dark) - what a fresh install opens with.</summary>
    public static SplashPalette Default { get; } = FromColors(new SkinColors(), isLight: false, cornerRadius: 14)!;

    /// <summary>The palette for a theme as it will be shown. Null if any colour fails to parse.</summary>
    public static SplashPalette? FromTheme(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return FromColors(theme.Colors, string.Equals(theme.Mode, "Light", StringComparison.OrdinalIgnoreCase), theme.RadiusLg);
    }

    private static SplashPalette? FromColors(SkinColors c, bool isLight, double cornerRadius)
    {
        if (Color.TryParse(c.Bg, out var surface) && Color.TryParse(c.Text, out var text) && Color.TryParse(c.TextMuted, out var muted)
            && Color.TryParse(c.TextFaint, out var faint) && Color.TryParse(c.Border, out var border) && Color.TryParse(c.Accent, out var accent)
            && Color.TryParse(c.AccentText, out var accentText) && Color.TryParse(c.AccentSoft, out var accentSoft))
        {
            return new SplashPalette(isLight, surface, text, muted, faint, border, accent, accentText, accentSoft, cornerRadius);
        }

        return null;
    }
}
