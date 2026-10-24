using Avalonia.Platform;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Splash;

/// <summary>
/// What <c>splash-theme.json</c> holds: the current theme's palette, plus the light and dark themes Auto mode would switch to and the
/// Auto settings, so the splash can show the theme this launch will actually open with.
/// </summary>
public sealed record SplashThemeSnapshot(
    ThemeAutoMode AutoMode,
    int DarkHour,
    int LightHour,
    SplashPalette Current,
    SplashPalette? Light,
    SplashPalette? Dark);

/// <summary>Picks the splash palette for this launch. Pure: the OS preference and the hour are passed in.</summary>
public static class SplashThemeChooser
{
    public static SplashPalette Choose(SplashThemeSnapshot? snapshot, PlatformThemeVariant? osVariant, int hour)
    {
        if (snapshot is null)
        {
            return SplashPalette.Default;
        }

        bool? wantLight = snapshot.AutoMode switch
        {
            ThemeAutoMode.FollowSystem when osVariant is { } os => os == PlatformThemeVariant.Light,
            ThemeAutoMode.Scheduled => !ThemeService.IsDarkPeriod(hour, snapshot.DarkHour, snapshot.LightHour),
            _ => null,
        };

        return wantLight switch
        {
            true => snapshot.Light ?? snapshot.Current,
            false => snapshot.Dark ?? snapshot.Current,
            null => snapshot.Current,
        };
    }
}
