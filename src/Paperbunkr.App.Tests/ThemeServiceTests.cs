using System.IO.Compression;
using System.Linq;
using Avalonia;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ThemeService"/> (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §3, renamed from <see cref="SkinService"/> by docs/superpowers/specs/2026-09-16-theme-system-
/// design.md). Joins <see cref="AvaloniaTestCollection"/> since <c>ApplyTheme</c> touches
/// <c>Application.Current.Resources</c>. Redirects <see cref="ThemePaths"/> to a temp folder and
/// uses an injected in-memory-database context factory (the same test-injection seam as
/// <see cref="CoverThumbnailService"/>) so tests never touch the real per-user themes folder or database.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeServiceTests : IDisposable
{
    private readonly string _originalInstalledDirectory;
    private readonly string _originalExtractedDirectory;
    private readonly string _installedDirectory;
    private readonly string _extractedDirectory;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ThemeServiceTests()
    {
        _originalInstalledDirectory = ThemePaths.InstalledDirectory;
        _originalExtractedDirectory = ThemePaths.ExtractedDirectory;

        string root = Path.Combine(Path.GetTempPath(), $"paperbunkr_skins_test_{Guid.NewGuid():N}");
        _installedDirectory = Path.Combine(root, "skins");
        _extractedDirectory = Path.Combine(root, "skins-extracted");
        ThemePaths.InstalledDirectory = _installedDirectory;
        ThemePaths.ExtractedDirectory = _extractedDirectory;

        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_skins_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        ThemePaths.InstalledDirectory = _originalInstalledDirectory;
        ThemePaths.ExtractedDirectory = _originalExtractedDirectory;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(Path.GetDirectoryName(_installedDirectory)))
            {
                Directory.Delete(Path.GetDirectoryName(_installedDirectory)!, recursive: true);
            }

            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private ThemeService CreateService() => new(() => new PaperbunkrDbContext(_dbOptions));

    private ThemeService CreateService(Func<DateTimeOffset> now) => new(() => new PaperbunkrDbContext(_dbOptions), now);

    /// <summary>Builds a real .crpck (plain ZIP) fixture, same "generate via the real code path" precedent as <see cref="CbzFixture"/>.</summary>
    private static string CreateCrpckFixture(string key, string themeJson)
    {
        string crpckPath = Path.Combine(Path.GetTempPath(), $"{key}_{Guid.NewGuid():N}.crpck");
        using (var stream = new FileStream(crpckPath, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("theme.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(themeJson);
        }

        return crpckPath;
    }

    /// <summary>Updated for docs/superpowers/specs/2026-09-16-theme-system-design.md - 10 built-ins
    /// now (the original 5 + Daylight/Overcast/Maximum Contrast/Color-Blind Safe/Matrix), not just
    /// Default; the count keeps growing as the catalog does, so this asserts membership more than
    /// an exact magic number where practical, but keeps one exact-count check as a real regression
    /// guard against a built-in silently going unreachable (the exact bug windows_11 originally had).</summary>
    [Fact]
    public void GetAvailableThemes_ListsAllBuiltIns_WhenNothingInstalled()
    {
        var service = CreateService();

        var themes = service.GetAvailableThemes();

        Assert.Equal(10, themes.Count);
        Assert.Contains(themes, t => t.Key == ThemeService.DefaultThemeKey && t.IsActive);
        Assert.Contains(themes, t => t.Key == "windows_11");
        Assert.Contains(themes, t => t.Key == "cool_technical");
        Assert.Contains(themes, t => t.Key == "vibrant_pop");
        Assert.Contains(themes, t => t.Key == "vintage_paperback");
    }

    /// <summary>docs/superpowers/specs/2026-09-07-appearance-redesign-design.md - the preview-card
    /// brushes GetAvailableThemes now populates must actually reflect each theme's real theme.json
    /// colors, not just be non-null.</summary>
    [Fact]
    public void GetAvailableThemes_PopulatesPreviewBrushes_MatchingTheme()
    {
        var service = CreateService();

        var themes = service.GetAvailableThemes();

        var defaultTheme = themes.Single(t => t.Key == ThemeService.DefaultThemeKey);
        Assert.Equal(Avalonia.Media.Color.Parse("#0A0B0D"), ((Avalonia.Media.SolidColorBrush)defaultTheme.BackgroundBrush).Color);
        Assert.Equal(Avalonia.Media.Color.Parse("#131519"), ((Avalonia.Media.SolidColorBrush)defaultTheme.ChromeBrush).Color);
        Assert.Equal(Avalonia.Media.Color.Parse("#C9803F"), ((Avalonia.Media.SolidColorBrush)defaultTheme.AccentBrush).Color);
        Assert.Equal(Avalonia.Media.Color.Parse("#1B1E24"), ((Avalonia.Media.SolidColorBrush)defaultTheme.SurfaceBrush).Color);
        Assert.Equal(Avalonia.Media.Color.Parse("#B3ADA0"), ((Avalonia.Media.SolidColorBrush)defaultTheme.TextMutedBrush).Color);

        var windows11 = themes.Single(t => t.Key == "windows_11");
        Assert.Equal(Avalonia.Media.Color.Parse("#0078D4"), ((Avalonia.Media.SolidColorBrush)windows11.AccentBrush).Color);
    }

    /// <summary>Windows 11 existed as a real theme.json on disk but was never actually reachable
    /// from GetAvailableThemes/LoadTheme before this fix (found during the codebase survey for the
    /// design doc above) - this is the regression test for that specific bug.</summary>
    [Theory]
    [InlineData("windows_11")]
    [InlineData("cool_technical")]
    [InlineData("vibrant_pop")]
    [InlineData("vintage_paperback")]
    public void LoadTheme_EveryNewBuiltIn_ParsesWithoutThrowing(string key)
    {
        var service = CreateService();

        var theme = service.LoadTheme(key);

        Assert.False(string.IsNullOrWhiteSpace(theme.Name));
        Assert.False(string.IsNullOrWhiteSpace(theme.Colors.Accent));
    }

    [Theory]
    [InlineData("cool_technical")]
    [InlineData("vibrant_pop")]
    [InlineData("vintage_paperback")]
    public void ApplyTheme_EveryNewBuiltIn_AppliesWithoutThrowing(string key)
    {
        var service = CreateService();

        service.ApplyTheme(key);

        Assert.Equal(key, service.GetAvailableThemes().Single(t => t.IsActive).Key);
    }

    /// <summary>
    /// A consumer that bakes a theme color into a raster (docs/superpowers/specs/
    /// 2026-09-08-home-navrail-visual-v2-design.md §3 - the Home masthead cover-wall) needs to know
    /// exactly when a theme switch happens, not just that resources changed underneath it.
    /// </summary>
    [Fact]
    public void ApplyTheme_RaisesThemeApplied_ExactlyOnce()
    {
        var service = CreateService();
        int raisedCount = 0;
        service.ThemeApplied += () => raisedCount++;

        service.ApplyTheme("cool_technical");

        Assert.Equal(1, raisedCount);
    }

    /// <summary>
    /// docs/superpowers/specs/2026-09-16-theme-system-design.md § Variant switching - previously
    /// App.axaml hardcoded RequestedThemeVariant="Dark" regardless of the active theme, so
    /// "windows_11"'s native FluentAvalonia chrome never actually followed its own light colors.
    /// </summary>
    [Theory]
    [InlineData("default", "Dark")]
    [InlineData("cool_technical", "Dark")]
    [InlineData("vibrant_pop", "Dark")]
    [InlineData("vintage_paperback", "Dark")]
    [InlineData("windows_11", "Light")]
    public void ApplyTheme_SetsRequestedThemeVariant_MatchingThemeMode(string key, string expectedVariantName)
    {
        var service = CreateService();

        service.ApplyTheme(key);

        // RequestedThemeVariant is thread-owned; ThemeService skips the set (and reading it here would
        // throw) on test-runner threads the dispatcher doesn't own. Only assert where it's meaningful.
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return;
        }

        var expectedVariant = expectedVariantName == "Light" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
        Assert.Equal(expectedVariant, Avalonia.Application.Current!.RequestedThemeVariant);
    }

    [Fact]
    public void MatrixRainEnabled_DefaultsTrue_PersistsAndRaisesChangedEvent()
    {
        var service = CreateService();
        Assert.True(service.GetMatrixRainEnabled());

        int raised = 0;
        service.MatrixRainEnabledChanged += () => raised++;

        service.SetMatrixRainEnabled(false);

        Assert.False(service.GetMatrixRainEnabled());
        Assert.Equal(1, raised);

        service.SetMatrixRainEnabled(true);

        Assert.True(service.GetMatrixRainEnabled());
        Assert.Equal(2, raised);
    }

    /// <summary>The hover/focus ring (BoxShadows can't embed a DynamicResource) is rebuilt in code from the
    /// active theme's glow color - regression for the ring staying hardcoded orange under every theme.</summary>
    [Fact]
    public void ApplyTheme_RebuildsGlowRing_FromThemeGlowColor()
    {
        var service = CreateService();

        service.ApplyTheme("matrix");

        var ring = Assert.IsType<Avalonia.Media.BoxShadows>(Avalonia.Application.Current!.Resources["PbGlowRing"]);
        Assert.Equal(1, ring.Count);
        Assert.Equal(4, ring[0].Spread);
        var glow = Avalonia.Media.Color.Parse(service.LoadTheme("matrix").Colors.Glow);
        Assert.Equal(Avalonia.Media.Color.FromArgb(0x99, glow.R, glow.G, glow.B), ring[0].Color);
    }

    [Fact]
    public void SetTrueBlackDark_On_OverwritesFiveSurfaceTokens_UnderDarkTheme()
    {
        var service = CreateService();
        service.ApplyTheme("default");

        service.SetTrueBlackDark(true);

        var resources = Avalonia.Application.Current!.Resources;
        Assert.Equal(Avalonia.Media.Color.Parse("#000000"), resources["PbBgColor"]);
        Assert.Equal(Avalonia.Media.Color.Parse("#000000"), resources["PbChromeColor"]);
        Assert.Equal(Avalonia.Media.Color.Parse("#000000"), resources["PbSurface0Color"]);
        Assert.Equal(Avalonia.Media.Color.Parse("#000000"), resources["PbSurface1Color"]);
        Assert.Equal(Avalonia.Media.Color.Parse("#000000"), resources["PbSurface2Color"]);
        Assert.Equal(Avalonia.Media.Color.Parse("#000000"), resources["PbSurface3Color"]);
    }

    [Fact]
    public void SetTrueBlackDark_Off_RestoresRealThemeValues_ViaFullApplyTheme()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        service.SetTrueBlackDark(true);

        service.SetTrueBlackDark(false);

        var resources = Avalonia.Application.Current!.Resources;
        Assert.Equal(Avalonia.Media.Color.Parse("#0A0B0D"), resources["PbBgColor"]);
        Assert.Equal(Avalonia.Media.Color.Parse("#131519"), resources["PbChromeColor"]);
    }

    [Fact]
    public void SetTrueBlackDark_On_IsNoOp_UnderLightTheme()
    {
        var service = CreateService();
        service.ApplyTheme("windows_11");

        service.SetTrueBlackDark(true);

        var resources = Avalonia.Application.Current!.Resources;
        Assert.Equal(Avalonia.Media.Color.Parse("#F3F3F3"), resources["PbBgColor"]);
    }

    [Fact]
    public void SetTrueBlackReaderSuspend_LeavesPersistedSettingUntouched_OnlyLiveResources()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        service.SetTrueBlackDark(true);

        service.SetTrueBlackReaderSuspend(suspend: true);
        var resourcesSuspended = Avalonia.Application.Current!.Resources;
        Assert.Equal(Avalonia.Media.Color.Parse("#0A0B0D"), resourcesSuspended["PbBgColor"]);

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.True(context.GetOrCreateAppSettings().TrueBlackDark);
        }

        service.SetTrueBlackReaderSuspend(suspend: false);
        var resourcesResumed = Avalonia.Application.Current!.Resources;
        Assert.Equal(Avalonia.Media.Color.Parse("#000000"), resourcesResumed["PbBgColor"]);
    }

    [Fact]
    public void ApplyTheme_UpdatesLastLightAndDarkThemeKeys_PerAppliedThemeMode()
    {
        var service = CreateService();

        service.ApplyTheme("cool_technical");
        service.ApplyTheme("windows_11");

        using var context = new PaperbunkrDbContext(_dbOptions);
        var settings = context.GetOrCreateAppSettings();
        Assert.Equal("cool_technical", settings.LastDarkThemeKey);
        Assert.Equal("windows_11", settings.LastLightThemeKey);
    }

    /// <summary>Real bug caught in review: a fixed "always darken" accent-override derivation
    /// fails whenever the active theme is Dark - this covers both directions explicitly.</summary>
    [Fact]
    public void AccentOverride_UnderLightTheme_DarkensToReachContrast()
    {
        var service = CreateService();
        service.ApplyTheme("windows_11"); // bg #F3F3F3

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().AccentOverrideHex = "#FFD700"; // bright gold, fails 4.5:1 as-is
            context.SaveChanges();
        }
        service.ApplyTheme("windows_11");

        var resources = Avalonia.Application.Current!.Resources;
        var bg = Avalonia.Media.Color.Parse("#F3F3F3");
        var accentText = (Avalonia.Media.Color)resources["PbAccentTextColor"]!;
        Assert.True(ContrastRatioForTest(accentText, bg) >= 4.5);
    }

    [Fact]
    public void AccentOverride_UnderDarkTheme_LightensToReachContrast()
    {
        var service = CreateService();
        service.ApplyTheme("default"); // bg #0A0B0D

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().AccentOverrideHex = "#1A1A2E"; // dark navy, fails 4.5:1 as-is
            context.SaveChanges();
        }
        service.ApplyTheme("default");

        var resources = Avalonia.Application.Current!.Resources;
        var bg = Avalonia.Media.Color.Parse("#0A0B0D");
        var accentText = (Avalonia.Media.Color)resources["PbAccentTextColor"]!;
        Assert.True(ContrastRatioForTest(accentText, bg) >= 4.5);
    }

    private static double ContrastRatioForTest(Avalonia.Media.Color a, Avalonia.Media.Color b)
    {
        static double Lin(byte channel)
        {
            double v = channel / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        static double Lum(Avalonia.Media.Color c) => (0.2126 * Lin(c.R)) + (0.7152 * Lin(c.G)) + (0.0722 * Lin(c.B));

        double l1 = Lum(a), l2 = Lum(b);
        if (l2 > l1) (l1, l2) = (l2, l1);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    [Fact]
    public void TryInstallSkin_RoundTrips_InstallExtractParseApply()
    {
        string crpckPath = CreateCrpckFixture("windows_11", """
            {"name":"Windows 11","colors":{"bg":"#111111","chrome":"#222222","border":"#333333","text":"#EEEEEE","textMuted":"#CCCCCC","textFaint":"#999999","accent":"#4488FF","accentText":"#66AAFF","accentSoft":"#204488FF","badge":"#FFAA00","badgeText":"#000000","success":"#33AA55"},"spacingUnit":4,"radius":8,"icons":{}}
            """);

        try
        {
            var service = CreateService();

            bool installed = service.TryInstallSkin(crpckPath, out string? error);
            Assert.True(installed);
            Assert.Null(error);

            string key = Path.GetFileNameWithoutExtension(crpckPath);
            var themes = service.GetAvailableThemes();
            Assert.Contains(themes, t => t.Key == key && t.Name == "Windows 11");

            service.ApplyTheme(key);

            var refreshed = service.GetAvailableThemes();
            Assert.True(refreshed.Single(t => t.Key == key).IsActive);
            Assert.False(refreshed.Single(t => t.Key == ThemeService.DefaultThemeKey).IsActive);

            var resources = Avalonia.Application.Current!.Resources;
            Assert.Equal(Avalonia.Media.Color.Parse("#111111"), resources["PbBgColor"]);

            // This fixture predates the elevation-scale expansion (docs/superpowers/specs/2026-08-24-
            // design-language-foundation-design.md) and deliberately omits surface0/glow/radiusSm -
            // proves an older/third-party theme.json still loads and falls back to SkinColors'
            // code-level defaults instead of failing or leaving stale resources behind.
            Assert.Equal(Avalonia.Media.Color.Parse("#000000"), resources["PbSurface0Color"]);
            Assert.Equal(Avalonia.Media.Color.Parse("#66E0995A"), resources["PbGlowColor"]);
            Assert.Equal(new CornerRadius(5), resources["PbRadiusSm"]);

            // docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md §8 - ChartBlue/
            // ChartViolet are additive the same way; this fixture predates them too.
            Assert.Equal(Avalonia.Media.Color.Parse("#5B8DBE"), resources["PbChartBlueColor"]);
            Assert.Equal(Avalonia.Media.Color.Parse("#9B7EBD"), resources["PbChartVioletColor"]);
        }
        finally
        {
            if (File.Exists(crpckPath)) File.Delete(crpckPath);
        }
    }

    [Fact]
    public void TryInstallSkin_RejectsMalformedThemeJson_WithoutCrashing()
    {
        string crpckPath = CreateCrpckFixture("broken", "{ not valid json");

        try
        {
            var service = CreateService();

            bool installed = service.TryInstallSkin(crpckPath, out string? error);

            Assert.False(installed);
            Assert.NotNull(error);

            string key = Path.GetFileNameWithoutExtension(crpckPath);
            Assert.DoesNotContain(service.GetAvailableThemes(), t => t.Key == key);
        }
        finally
        {
            if (File.Exists(crpckPath)) File.Delete(crpckPath);
        }
    }

    /// <summary>docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md §8 - each built-in
    /// theme defines its own ChartBlue/ChartViolet rather than falling back to the default's.</summary>
    [Theory]
    [InlineData("windows_11", "#038387", "#8764B8")]
    [InlineData("cool_technical", "#6E7FD8", "#B085D9")]
    [InlineData("vibrant_pop", "#4E9DE8", "#B14EE8")]
    [InlineData("vintage_paperback", "#5B7A94", "#7A5B78")]
    public void ApplyTheme_SetsChartColors_PerTheme(string key, string expectedBlue, string expectedViolet)
    {
        var service = CreateService();

        service.ApplyTheme(key);

        var resources = Avalonia.Application.Current!.Resources;
        Assert.Equal(Avalonia.Media.Color.Parse(expectedBlue), resources["PbChartBlueColor"]);
        Assert.Equal(Avalonia.Media.Color.Parse(expectedViolet), resources["PbChartVioletColor"]);
    }

    [Fact]
    public void GetIcon_ReturnsNull_ForUndefinedIcon()
    {
        var service = CreateService();

        var icon = service.GetIcon(ThemeService.DefaultThemeKey, "does-not-exist");

        Assert.Null(icon);
    }

    /// <summary>docs/superpowers/specs/2026-09-16-theme-system-design.md § New theme catalog /
    /// § Extended scope - Daylight/Overcast/Maximum Contrast/Color-Blind Safe load without throwing,
    /// same regression-proof shape as LoadTheme_EveryNewBuiltIn_ParsesWithoutThrowing above.</summary>
    [Theory]
    [InlineData("daylight", "Light")]
    [InlineData("overcast", "Light")]
    [InlineData("maximum_contrast", "Dark")]
    [InlineData("colorblind_safe", "Dark")]
    [InlineData("matrix", "Dark")]
    public void LoadTheme_NewCatalogEntries_ParseWithCorrectMode(string key, string expectedMode)
    {
        var service = CreateService();

        var theme = service.LoadTheme(key);

        Assert.False(string.IsNullOrWhiteSpace(theme.Name));
        Assert.Equal(expectedMode, theme.Mode);
    }

    [Theory]
    [InlineData("daylight")]
    [InlineData("overcast")]
    [InlineData("maximum_contrast")]
    [InlineData("colorblind_safe")]
    [InlineData("matrix")]
    public void ApplyTheme_NewCatalogEntries_AppliesWithoutThrowing(string key)
    {
        var service = CreateService();

        service.ApplyTheme(key);

        Assert.Equal(key, service.GetAvailableThemes().Single(t => t.IsActive).Key);
    }

    /// <summary>Matrix-specific fields (§ Extended scope) - "category" is backend-only/not a mode
    /// value (Q9's own resolution: exactly Light/Dark, Matrix tagged Dark + a separate category),
    /// matrixGlyphs/defaultFontFamily are what <c>MatrixRainOverlay</c>/font precedence read.</summary>
    [Fact]
    public void LoadTheme_Matrix_HasCategoryAndGlyphsAndFont()
    {
        var service = CreateService();

        var theme = service.LoadTheme("matrix");

        Assert.Equal("matrix", theme.Category);
        Assert.NotNull(theme.MatrixGlyphs);
        Assert.NotEmpty(theme.MatrixGlyphs!);
        Assert.Equal("Cascadia Code, Consolas, monospace", theme.DefaultFontFamily);
    }

    [Fact]
    public void BatteryStatusInterop_IsThrottled_DoesNotThrow()
    {
        // Real device state on the test machine - non-deterministic result, only asserting it
        // never throws (the whole point of moving this off the render thread onto a 30s UI-thread
        // poll is that a throwing/slow call here must never be able to wedge anything time-critical).
        var exception = Record.Exception(() => BatteryStatusInterop.IsThrottled());
        Assert.Null(exception);
    }

    [Fact]
    public void MatrixRainOverlay_IsOpaqueBackplateProperty_RoundTrips()
    {
        var control = new Avalonia.Controls.Border();

        Assert.False(MatrixRainOverlay.GetIsOpaqueBackplate(control));

        MatrixRainOverlay.SetIsOpaqueBackplate(control, true);
        Assert.True(MatrixRainOverlay.GetIsOpaqueBackplate(control));

        MatrixRainOverlay.SetIsOpaqueBackplate(control, false);
        Assert.False(MatrixRainOverlay.GetIsOpaqueBackplate(control));
    }

    // ===================== Auto mode (docs/superpowers/specs/2026-09-16-theme-system-design.md
    // § Extended scope) =====================

    [Fact]
    public void SetThemeAutoMode_Persists()
    {
        var service = CreateService();

        service.SetThemeAutoMode(Paperbunkr.Data.Entities.ThemeAutoMode.Scheduled);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(Paperbunkr.Data.Entities.ThemeAutoMode.Scheduled, context.GetOrCreateAppSettings().ThemeAutoMode);
    }

    /// <summary>
    /// Regression guard for the 2026-09-10 freeze fix - reuses the exact reflection approach
    /// <c>FluentAvaloniaWorkarounds.FindColorValuesChangedField</c> already established to find the
    /// backing delegate. FollowSystem must subscribe on enable and, critically, unsubscribe again on
    /// disable - a leaked subscriber here would be a second surface for the same class of bug this
    /// project already paid a debugging session to find and fix once.
    /// </summary>
    [Fact]
    public void SetThemeAutoMode_FollowSystem_SubscribesAndUnsubscribes_ColorValuesChanged()
    {
        var settings = Avalonia.Application.Current!.PlatformSettings;
        Assert.NotNull(settings);
        var field = FindColorValuesChangedField(settings!);
        Assert.NotNull(field);

        int CountSubscribers() => (field!.GetValue(settings) as Delegate)?.GetInvocationList().Length ?? 0;

        var service = CreateService();
        int before = CountSubscribers();

        service.SetThemeAutoMode(Paperbunkr.Data.Entities.ThemeAutoMode.FollowSystem);
        Assert.True(CountSubscribers() > before);

        service.SetThemeAutoMode(Paperbunkr.Data.Entities.ThemeAutoMode.Off);
        Assert.Equal(before, CountSubscribers());
    }

    private static System.Reflection.FieldInfo? FindColorValuesChangedField(object settings)
    {
        for (var type = settings.GetType(); type is not null; type = type.BaseType)
        {
            var field = type
                .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .FirstOrDefault(f => typeof(Delegate).IsAssignableFrom(f.FieldType) && f.Name.Contains("ColorValuesChanged", StringComparison.Ordinal));
            if (field is not null)
            {
                return field;
            }
        }
        return null;
    }

    [Fact]
    public void Scheduled_AtDarkHour_SwitchesToLastDarkTheme_AndRaisesCrossfade()
    {
        var fixedNow = new DateTimeOffset(2026, 1, 1, 21, 0, 0, TimeSpan.Zero); // 9pm - default dark window 20:00-07:00
        var service = CreateService(() => fixedNow);
        service.ApplyTheme("cool_technical"); // sets LastDarkThemeKey
        service.ApplyTheme("windows_11"); // sets LastLightThemeKey, leaves this the active theme
        int crossfadeRaised = 0;
        service.ScheduledThemeCrossfadeRequested += () => crossfadeRaised++;

        service.SetThemeAutoMode(Paperbunkr.Data.Entities.ThemeAutoMode.Scheduled);

        Assert.Equal("cool_technical", service.GetActiveThemeKey());
        Assert.Equal(1, crossfadeRaised);
    }

    [Fact]
    public void Scheduled_AtLightHour_SwitchesToLastLightTheme()
    {
        var fixedNow = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero); // 9am - inside the default light window
        var service = CreateService(() => fixedNow);
        service.ApplyTheme("windows_11");
        service.ApplyTheme("cool_technical");

        service.SetThemeAutoMode(Paperbunkr.Data.Entities.ThemeAutoMode.Scheduled);

        Assert.Equal("windows_11", service.GetActiveThemeKey());
    }

    [Fact]
    public void Scheduled_AlreadyOnTargetTheme_DoesNotRaiseCrossfade()
    {
        var fixedNow = new DateTimeOffset(2026, 1, 1, 21, 0, 0, TimeSpan.Zero);
        var service = CreateService(() => fixedNow);
        service.ApplyTheme("windows_11"); // LastLightThemeKey
        service.ApplyTheme("cool_technical"); // LastDarkThemeKey, already active - already the 9pm target
        int crossfadeRaised = 0;
        service.ScheduledThemeCrossfadeRequested += () => crossfadeRaised++;

        service.SetThemeAutoMode(Paperbunkr.Data.Entities.ThemeAutoMode.Scheduled);

        Assert.Equal(0, crossfadeRaised);
    }

    [Fact]
    public void Scheduled_AtTrueBlackAutoHour_EnablesTrueBlackDark()
    {
        var fixedNow = new DateTimeOffset(2026, 1, 1, 22, 0, 0, TimeSpan.Zero);
        var service = CreateService(() => fixedNow);
        service.ApplyTheme("default");
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().TrueBlackAutoHour = 22;
            context.SaveChanges();
        }

        service.SetThemeAutoMode(Paperbunkr.Data.Entities.ThemeAutoMode.Scheduled);

        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.True(verify.GetOrCreateAppSettings().TrueBlackDark);
    }

    // ===================== Font precedence / backdrop / scrollbar tokens (§ Extended scope) =====================

    [Fact]
    public void ApplyFont_ExplicitOverride_WinsOverThemeDefault()
    {
        var service = CreateService();
        service.ApplyTheme("matrix"); // theme.DefaultFontFamily = "Cascadia Code, Consolas, monospace"

        service.ApplyFont("Arial");

        var resolved = (Avalonia.Media.FontFamily)Avalonia.Application.Current!.Resources["PbFontFamily"]!;
        Assert.Contains("Arial", resolved.Name);
    }

    [Fact]
    public void ApplyTheme_NoExplicitOverride_FallsBackToThemeDefaultFont()
    {
        var service = CreateService();
        service.ApplyFont(null); // clear any override first
        service.ApplyTheme("default"); // no defaultFontFamily - resolves to the hardcoded default

        service.ApplyTheme("matrix");

        var resolved = (Avalonia.Media.FontFamily)Avalonia.Application.Current!.Resources["PbFontFamily"]!;
        Assert.Contains("Cascadia Code", resolved.Name);
    }

    [Fact]
    public void ApplyTheme_NoExplicitOverride_NoThemeDefault_FallsBackToHardcodedDefault()
    {
        var service = CreateService();
        service.ApplyFont(null);
        service.ApplyTheme("matrix"); // has a theme default

        service.ApplyTheme("default"); // no theme default - must NOT keep Matrix's font

        var resolved = (Avalonia.Media.FontFamily)Avalonia.Application.Current!.Resources["PbFontFamily"]!;
        Assert.Contains("Source Serif 4", resolved.Name);
    }

    [Fact]
    public void ApplyTheme_RaisesWindowBackdropRequested_WithThemesOwnBackdrop()
    {
        var service = CreateService();
        string? received = null;
        service.WindowBackdropRequested += backdrop => received = backdrop;

        service.ApplyTheme("windows_11");
        Assert.Equal("Mica", received);

        service.ApplyTheme("default");
        Assert.Equal("None", received); // default omits windowBackdrop - falls back to the schema's own "None" default
    }
}
