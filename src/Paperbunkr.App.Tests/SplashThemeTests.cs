using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Splash;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-10-22-splash-theme-redesign-design.md - the splash follows the theme, light or dark.</summary>
public class SplashThemeChooserTests
{
    private static SplashPalette P(bool light, string surface) =>
        SplashPalette.Default with { IsLight = light, Surface = Color.Parse(surface) };

    private static readonly SplashPalette Current = P(false, "#111111");
    private static readonly SplashPalette Light = P(true, "#EEEEEE");
    private static readonly SplashPalette Dark = P(false, "#222222");

    private static SplashThemeSnapshot Snap(ThemeAutoMode mode, int dark = 20, int light = 7, bool withTargets = true) =>
        new(mode, dark, light, Current, withTargets ? Light : null, withTargets ? Dark : null);

    [Fact]
    public void NoSnapshot_IsDefault() => Assert.Same(SplashPalette.Default, SplashThemeChooser.Choose(null, PlatformThemeVariant.Light, 12));

    [Fact]
    public void Off_IsCurrent_WhateverTheOs() =>
        Assert.Same(Current, SplashThemeChooser.Choose(Snap(ThemeAutoMode.Off), PlatformThemeVariant.Light, 12));

    [Theory]
    [InlineData(PlatformThemeVariant.Light, true)]
    [InlineData(PlatformThemeVariant.Dark, false)]
    public void FollowSystem_UsesOsVariant(PlatformThemeVariant os, bool expectLight) =>
        Assert.Same(expectLight ? Light : Dark, SplashThemeChooser.Choose(Snap(ThemeAutoMode.FollowSystem), os, 12));

    [Fact]
    public void FollowSystem_UnknownOs_IsCurrent() =>
        Assert.Same(Current, SplashThemeChooser.Choose(Snap(ThemeAutoMode.FollowSystem), null, 12));

    [Theory]
    [InlineData(12, false)] // day
    [InlineData(21, true)]  // after the dark hour
    [InlineData(2, true)]   // past midnight, before the light hour
    [InlineData(7, false)]  // the light hour itself is light
    public void Scheduled_WrapsPastMidnight(int hour, bool expectDark) =>
        Assert.Same(expectDark ? Dark : Light, SplashThemeChooser.Choose(Snap(ThemeAutoMode.Scheduled), null, hour));

    [Theory]
    [InlineData(10, true)]
    [InlineData(16, false)]
    public void Scheduled_DarkHourBeforeLightHour(int hour, bool expectDark) =>
        Assert.Same(expectDark ? Dark : Light, SplashThemeChooser.Choose(Snap(ThemeAutoMode.Scheduled, dark: 9, light: 15), null, hour));

    [Fact]
    public void MissingTarget_FallsBackToCurrent() =>
        Assert.Same(Current, SplashThemeChooser.Choose(Snap(ThemeAutoMode.FollowSystem, withTargets: false), PlatformThemeVariant.Light, 12));
}

public class SplashThemeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pb_splash_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void RoundTrips()
    {
        var light = SplashPalette.Default with { IsLight = true, Surface = Color.Parse("#F7F3EC"), AccentSoft = Color.Parse("#1F985A27") };
        var snapshot = new SplashThemeSnapshot(ThemeAutoMode.Scheduled, 21, 6, SplashPalette.Default, light, null);
        var store = new SplashThemeStore(Path.Combine(_dir, "splash-theme.json"));

        store.Save(snapshot);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(ThemeAutoMode.Scheduled, loaded!.AutoMode);
        Assert.Equal(21, loaded.DarkHour);
        Assert.Equal(6, loaded.LightHour);
        Assert.Equal(SplashPalette.Default, loaded.Current);
        Assert.Equal(light, loaded.Light);
        Assert.Null(loaded.Dark);
    }

    [Fact]
    public void MissingFile_IsNull() => Assert.Null(new SplashThemeStore(Path.Combine(_dir, "nope.json")).Load());

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":99,\"current\":{}}")]
    [InlineData("{\"version\":1,\"current\":{\"surface\":\"nonsense\"}}")]
    [InlineData("{\"version\":1}")]
    public void UnusableFile_IsNull(string text)
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "splash-theme.json");
        File.WriteAllText(path, text);

        Assert.Null(new SplashThemeStore(path).Load());
    }
}

[Collection(nameof(AvaloniaTestCollection))]
public class ThemeServiceSplashSnapshotTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_splash_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public ThemeServiceSplashSnapshotTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.Migrate();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void WritesActiveThemeAutoSettingsAndTargets()
    {
        using (var context = new PaperbunkrDbContext(_options))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.ActiveThemeKey = "daylight";
            settings.LastLightThemeKey = "daylight";
            settings.LastDarkThemeKey = "default";
            settings.ThemeAutoMode = ThemeAutoMode.Scheduled;
            settings.ThemeScheduledDarkHour = 19;
            settings.ThemeScheduledLightHour = 8;
            context.SaveChanges();
        }

        var store = new SplashThemeStore();
        var service = new ThemeService(() => new PaperbunkrDbContext(_options));
        service.UseSplashStore(store);

        service.SaveSplashSnapshot();
        var snapshot = store.Load();

        Assert.NotNull(snapshot);
        Assert.Equal(ThemeAutoMode.Scheduled, snapshot!.AutoMode);
        Assert.Equal(19, snapshot.DarkHour);
        Assert.Equal(8, snapshot.LightHour);
        Assert.True(snapshot.Current.IsLight);
        Assert.True(snapshot.Light!.IsLight);
        Assert.False(snapshot.Dark!.IsLight);
        Assert.Equal(Color.Parse("#F7F3EC"), snapshot.Current.Surface);
    }

    [Fact]
    public void WithoutAStore_WritesNothing()
    {
        var service = new ThemeService(() => new PaperbunkrDbContext(_options));
        service.SaveSplashSnapshot(); // must not throw or touch the real file
    }
}

[Collection(nameof(AvaloniaTestCollection))]
public class SplashWindowPaletteTests
{
    [Fact]
    public void LightPalette_LandsInTheWindowsOwnResources()
    {
        var light = SplashPalette.Default with { IsLight = true, Surface = Color.Parse("#F7F3EC"), Text = Color.Parse("#2A2620") };
        var window = new SplashWindow(reducedMotion: true, ambientMotionEnabled: false, light) { DataContext = new SplashViewModel() };
        window.Show();

        var card = window.FindControl<Border>("Card")!;
        Assert.Equal(Color.Parse("#F7F3EC"), Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, window.RequestedThemeVariant);

        window.Close();
    }

    /// <summary>Not an assertion: the splash in Default, Daylight, Matrix and Maximum Contrast, written when PB_RENDER_OUT is set.</summary>
    [Fact]
    public void RenderSplashes()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        var themes = new ThemeService(() => throw new InvalidOperationException("no database in a render test"));
        foreach (string theme in new[] { "default", "daylight", "matrix", "maximum_contrast" })
        {
            Theming.FoundationStyleTests.WithFoundation(theme, () =>
            {
                var palette = SplashPalette.FromTheme(themes.LoadTheme(theme))!;
                var vm = new SplashViewModel();
                vm.ReportPhase(1, 4, "Applying database migrations…");
                var window = new SplashWindow(reducedMotion: true, ambientMotionEnabled: false, palette) { DataContext = vm };
                window.Show();
                FocusTestHarness.RunLayout(window);
                double scale = double.TryParse(Environment.GetEnvironmentVariable("PB_RENDER_SCALE"), System.Globalization.CultureInfo.InvariantCulture, out double sc) ? sc : 1;
                var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)(512 * scale), (int)(372 * scale)), new Avalonia.Vector(96 * scale, 96 * scale));
                rtb.Render(window);
                rtb.Save(Path.Combine(outDir, $"splash-{theme}@{scale}.png"));
                window.Close();
            });
        }
    }
}
