using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Startup's move off the themes retired on 2026-11-09 (docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md "Migration"): each retired
/// theme lands on its nearest kept theme with its accent carried, an OLED variant becomes dark + True black, bare keys of families that gained
/// variants are written in full, and a second run changes nothing.
/// </summary>
public class ThemeKeyMigrationTests
{
    // The catalog's answer in slice 3: these families gained a second variant, so their bare key stands for the first one.
    private static string? FullKeyOf(string family) => family switch
    {
        "windows_11" => "windows_11:light",
        "matrix" => "matrix:dark",
        "crimson_ink" => "crimson_ink:dark",
        _ => null,
    };

    private static string? NoVariants(string family) => null;

    [Theory]
    [InlineData("cool_technical", "graphite:dark", "#5EA7F0")]
    [InlineData("vibrant_pop", "default", "#E85D4E")]
    [InlineData("vintage_paperback", "reading_room:lamplight", "#8C3B2E")]
    [InlineData("overcast", "nordic_frost:light", "#3469A6")]
    [InlineData("graphite:light", "windows_11", "#1A5FC4")]
    public void ARetiredTheme_MovesToItsNearestKeptTheme_AndKeepsItsAccent(string retired, string expected, string accent)
    {
        var settings = new AppSettings { ActiveThemeKey = retired };

        var result = ThemeKeyMigration.Apply(settings, NoVariants);

        Assert.True(result.Changed);
        Assert.Equal(expected, settings.ActiveThemeKey);
        Assert.Equal(accent, settings.AccentOverrideHex);
        Assert.Contains("retired", result.Notice);
    }

    [Fact]
    public void ARetiredTheme_DoesNotReplaceAnAccentTheUserAlreadyChose()
    {
        var settings = new AppSettings { ActiveThemeKey = "vibrant_pop", AccentOverrideHex = "#123456" };

        ThemeKeyMigration.Apply(settings, NoVariants);

        Assert.Equal("#123456", settings.AccentOverrideHex);
    }

    [Theory]
    [InlineData("abyss:oled", "abyss:dark")]
    [InlineData("tokyo_midnight:oled", "tokyo_midnight:dark")]
    [InlineData("graphite:oled", "graphite:dark")]
    public void AnOledVariant_BecomesDarkWithTrueBlackOn(string oled, string dark)
    {
        var settings = new AppSettings { ActiveThemeKey = oled };

        var result = ThemeKeyMigration.Apply(settings, NoVariants);

        Assert.Equal(dark, settings.ActiveThemeKey);
        Assert.True(settings.TrueBlackDark);
        Assert.Null(settings.AccentOverrideHex);
        Assert.Contains("True black", result.Notice);
    }

    [Fact]
    public void RememberedAutoSwitchKeys_MoveToo_WithoutAccentOrTrueBlackSideEffects()
    {
        var settings = new AppSettings { ActiveThemeKey = "default", LastDarkThemeKey = "vintage_paperback", LastLightThemeKey = "overcast" };

        var result = ThemeKeyMigration.Apply(settings, NoVariants);

        Assert.True(result.Changed);
        Assert.Equal("reading_room:lamplight", settings.LastDarkThemeKey);
        Assert.Equal("nordic_frost:light", settings.LastLightThemeKey);
        Assert.Null(settings.AccentOverrideHex);
        Assert.Null(result.Notice);                     // the look on screen did not move, so there is nothing to say
    }

    [Fact]
    public void ABareKeyOfAFamilyThatGainedVariants_IsWrittenInFull_Quietly()
    {
        var settings = new AppSettings { ActiveThemeKey = "matrix", LastLightThemeKey = "windows_11", LastDarkThemeKey = "crimson_ink" };

        var result = ThemeKeyMigration.Apply(settings, FullKeyOf);

        Assert.Equal("matrix:dark", settings.ActiveThemeKey);
        Assert.Equal("windows_11:light", settings.LastLightThemeKey);
        Assert.Equal("crimson_ink:dark", settings.LastDarkThemeKey);
        Assert.Null(result.Notice);
    }

    [Fact]
    public void GraphiteLight_EndsOnWindows11sFullKey_OnceWindows11HasTwoHalves()
    {
        var settings = new AppSettings { ActiveThemeKey = "graphite:light" };

        ThemeKeyMigration.Apply(settings, FullKeyOf);

        Assert.Equal("windows_11:light", settings.ActiveThemeKey);
    }

    [Fact]
    public void RunningTwice_ChangesNothingTheSecondTime()
    {
        var settings = new AppSettings { ActiveThemeKey = "cool_technical", LastLightThemeKey = "windows_11", TrueBlackDark = false };
        ThemeKeyMigration.Apply(settings, FullKeyOf);
        var snapshot = (settings.ActiveThemeKey, settings.LastLightThemeKey, settings.LastDarkThemeKey, settings.AccentOverrideHex, settings.TrueBlackDark);

        var second = ThemeKeyMigration.Apply(settings, FullKeyOf);

        Assert.False(second.Changed);
        Assert.Null(second.Notice);
        Assert.Equal(snapshot, (settings.ActiveThemeKey, settings.LastLightThemeKey, settings.LastDarkThemeKey, settings.AccentOverrideHex, settings.TrueBlackDark));
    }

    [Fact]
    public void AKeptOrUnknownTheme_IsLeftAlone()
    {
        var settings = new AppSettings { ActiveThemeKey = "my_installed_theme", LastDarkThemeKey = "abyss:dark" };

        var result = ThemeKeyMigration.Apply(settings, NoVariants);

        Assert.False(result.Changed);
        Assert.Equal("my_installed_theme", settings.ActiveThemeKey);
    }
}
