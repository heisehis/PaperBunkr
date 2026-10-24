using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;
using Paperbunkr.Data;
using static Paperbunkr.App.Tests.FocusTestHarness;
using Path = Avalonia.Controls.Shapes.Path;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// Phase 3 of the theme engine (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md): component-role skins, typography
/// roles, materials, semantic icons and the scrollbar metrics - and, for each, that a theme which styles none of it is unchanged.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeSkinTests : IDisposable
{
    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pb_theme_skin_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ThemeSkinTests()
    {
        Directory.CreateDirectory(_root);
        ThemePaths.InstalledDirectory = System.IO.Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = System.IO.Path.Combine(_root, "skins-extracted");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={System.IO.Path.Combine(_root, "s.db")}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        // Leave the process in the built-in look: no icon replacements, no theme-specific keys.
        try
        {
            CreateService().ApplyTheme("default");
        }
        catch (InvalidOperationException)
        {
        }

        ThemeIconSource.Set(null, null);
        ThemePaths.InstalledDirectory = _originalInstalledDirectory;
        ThemePaths.ExtractedDirectory = _originalExtractedDirectory;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ThemeService CreateService() => new(() => new PaperbunkrDbContext(_dbOptions));

    private void InstallTheme(string key, string themeJson, params (string Path, byte[] Content)[] assets)
    {
        string folder = System.IO.Path.Combine(ThemePaths.ExtractedDirectory, key);
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, "theme.json"), themeJson);
        foreach (var (path, content) in assets)
        {
            string full = System.IO.Path.Combine(folder, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content);
        }
    }

    private static string Theme(string body) =>
        $$"""{ "schemaVersion": 2, "id": "org.test.skin", "name": "Skin", "mode": "Dark", "base": { "palette": { "canvas": "#101418", "ink": "#E6EAF0", "accent": "#4DA3FF" }, {{body}} } }""";

    /// <summary>Zeroes the motion durations (what Reduce motion does) for the scope, so a brush transition has finished by the next layout.</summary>
    private static IDisposable InstantMotion()
    {
        var resources = Application.Current!.Resources;
        string[] keys = { "PbMotionFast", "PbMotionStandard", "PbMotionSlow", "PbMotionLarge" };
        var previous = keys.ToDictionary(k => k, k => resources.TryGetValue(k, out var v) ? v : null);
        foreach (string key in keys)
        {
            resources[key] = TimeSpan.Zero;
        }

        // ApplyTheme rewrites every motion token from the policy, so zeroing the resources alone lasts only until the next theme apply;
        // a zero time scale makes the policy itself instant (Motion v2 §4.4's PB_MOTION_SCALE seam).
        double previousScale = Paperbunkr.App.Theming.Motion.MotionPolicy.TimeScale;
        Paperbunkr.App.Theming.Motion.MotionPolicy.SetTimeScaleForTests(0);

        return new Restore(() =>
        {
            Paperbunkr.App.Theming.Motion.MotionPolicy.SetTimeScaleForTests(previousScale);
            foreach (var (key, value) in previous)
            {
                if (value is null)
                {
                    resources.Remove(key);
                }
                else
                {
                    resources[key] = value;
                }
            }
        });
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    private static object? Res(string key) => Application.Current!.Resources.TryGetValue(key, out var value) ? value : null;

    private static Color? Skin(string role, string state, string property) =>
        (ThemeStateStyleHost.Current.FirstOrDefault(s => s.Role == role && s.State == state && s.Property == property)?.Brush as ISolidColorBrush)?.Color;

    private static Color BrushColor(string key) => ((ISolidColorBrush)Res(key)!).Color;

    private static Color C(string hex) => Color.Parse(hex);

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        bitmap.Erase(SkiaSharp.SKColors.Teal);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // ===================== The mechanism the state rules rely on =====================

    [Fact]
    public void AnUnstyledButton_KeepsTheFrameworksOwnPressedLook_AndAStyledOneGetsItsSkin_Live()
    {
        InstallTheme("skin", Theme("""
            "components": { "button.primary": { "background": "#00AA00" } }
            """));
        var service = CreateService();

        WithThemeAndTokens(() =>
        {
            using var resources = AppResources();
            using var styles = AppStyles();
            using var instant = InstantMotion();
            service.ApplyTheme("default");

            var button = new Button { Classes = { "primary" }, Content = "Go" };
            var window = new Window { Width = 200, Height = 100, Content = button };
            window.Show();
            try
            {
                RunLayout(window);
                var presenter = button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                ((IPseudoClasses)button.Classes).Set(":pressed", true);
                RunLayout(window);

                var frameworkPressed = (presenter.Background as ISolidColorBrush)?.Color;
                Assert.NotNull(frameworkPressed);                               // nothing of ours blanks the framework's pressed fill
                Assert.Empty(ThemeStateStyleHost.Current);

                service.ApplyTheme("skin");
                RunLayout(window);
                Assert.Equal(ThemeColorMath.Darken(C("#00AA00"), 0.12), ((ISolidColorBrush)presenter.Background!).Color);   // the derived pressed skin, live

                service.ApplyTheme("default");
                RunLayout(window);
                Assert.Empty(ThemeStateStyleHost.Current);
                Assert.Equal(frameworkPressed, (presenter.Background as ISolidColorBrush)?.Color);   // and the framework's own look is back
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ===================== Component roles =====================

    [Fact]
    public void AnUnstyledComponent_GetsExactlyTheTokensItWasBoundToBefore()
    {
        CreateService().ApplyTheme("default");

        Assert.Equal(C("#C9803F"), BrushColor("PbButtonPrimaryBackground"));          // was PbAccentBrush
        Assert.Equal(C("#241505"), BrushColor("PbButtonPrimaryForeground"));          // was PbBadgeTextBrush
        Assert.Equal(C("#C9803F"), BrushColor("PbButtonPrimaryBorder"));
        Assert.Equal(C("#E0995A"), BrushColor("PbButtonPrimaryBackgroundHover"));     // was PbAccentTextBrush
        Assert.Equal(new CornerRadius(5), Res("PbButtonPrimaryRadius"));
        Assert.Equal(C("#131519"), BrushColor("PbButtonSecondaryBackground"));        // was PbSurface2Brush
        Assert.Equal(C("#1B1E24"), BrushColor("PbButtonSecondaryBackgroundHover"));   // was PbSurface3Brush
        Assert.Equal(Colors.Transparent, BrushColor("PbButtonGhostBackground"));      // was the literal Transparent
        Assert.Equal(C("#B3ADA0"), BrushColor("PbButtonGhostForeground"));            // was PbTextMutedBrush
        Assert.Equal(C("#C9803F"), BrushColor("PbButtonGhostBorderHover"));
        Assert.Equal(new CornerRadius(9), Res("PbChipRadius"));                       // the squircle chip radius, not a pill
        Assert.Equal(C("#D7AC4C"), BrushColor("PbChipBorderHover"));
        Assert.Equal(C("#1B1E24"), BrushColor("PbPanelFloatingBackground"));
        Assert.Equal(new CornerRadius(14), Res("PbPanelFloatingRadius"));

        // No state skin exists, so the framework's own pressed / disabled visuals are what shows.
        Assert.Empty(ThemeStateStyleHost.Current);
    }

    [Fact]
    public void AStyledComponent_GetsItsAuthoredStates_AndTheMissingOnesAreDerived()
    {
        InstallTheme("skin", Theme("""
            "components": { "button.primary": { "background": "#00AA00", "foreground": "#FFFFFF", "radius": 12, "states": { "hover": { "background": "#00CC00" } } } }
            """));
        var service = CreateService();

        service.ApplyTheme("skin");

        Assert.Equal(C("#00AA00"), BrushColor("PbButtonPrimaryBackground"));
        Assert.Equal(Colors.White, BrushColor("PbButtonPrimaryForeground"));
        Assert.Equal(new CornerRadius(12), Res("PbButtonPrimaryRadius"));
        Assert.Equal(C("#00CC00"), BrushColor("PbButtonPrimaryBackgroundHover"));        // authored
        Assert.Equal(C("#00CC00"), Skin("button.primary", "hover", "background"));
        Assert.Equal(ThemeColorMath.Darken(C("#00AA00"), 0.12), Skin("button.primary", "pressed", "background"));      // derived: dark theme, pressed goes darker
        Assert.Equal((byte)(255 * 0.4), Skin("button.primary", "disabled", "background")!.Value.A);                      // derived: faded

        // A component the theme did not touch still has no state skins.
        Assert.DoesNotContain(ThemeStateStyleHost.Current, s => s.Role == "button.secondary");

        service.ApplyTheme("default");
        Assert.Empty(ThemeStateStyleHost.Current);                                       // and they do not linger after switching away
        Assert.Equal(C("#C9803F"), BrushColor("PbButtonPrimaryBackground"));
    }

    [Fact]
    public void AUserComponentOverride_RestylesTheRole_ForEveryTheme()
    {
        var service = CreateService();
        service.ApplyTheme("default");

        Assert.True(service.SetOverride(ThemeOverrideScope.Global, ThemeTokenPaths.Component("button.primary", "normal", "background"), "#AA00AA"));

        Assert.Equal(C("#AA00AA"), BrushColor("PbButtonPrimaryBackground"));
        Assert.Equal(ThemeValueSource.UserComponent, service.Current!.Component("button.primary", "normal", "background")!.Source);
        Assert.NotNull(Skin("button.primary", "pressed", "background"));                 // styled now, so its states are derived

        service.ApplyTheme("daylight");
        Assert.Equal(C("#AA00AA"), BrushColor("PbButtonPrimaryBackground"));
    }

    [Fact]
    public void APrimaryButton_OnScreen_FollowsItsRole()
    {
        InstallTheme("skin", Theme("""
            "components": { "button.primary": { "background": "#00AA00", "radius": 12 } }
            """));
        var service = CreateService();
        TestAppBuilder.EnsureInitialized();

        using var resources = AppResources();
        using var styles = AppStyles();
        using var instant = InstantMotion();                                          // the style animates Background; read the settled value
        var button = new Button { Classes = { "primary" }, Content = "Go" };
        var window = new Window { Width = 200, Height = 100, Content = button };
        window.Show();
        try
        {
            service.ApplyTheme("default");
            RunLayout(window);
            Assert.Equal(C("#C9803F"), ((ISolidColorBrush)button.Background!).Color);
            Assert.Equal(new CornerRadius(5), button.CornerRadius);

            service.ApplyTheme("skin");                                                   // live: no restart, no re-creation of the button
            RunLayout(window);
            Assert.Equal(C("#00AA00"), ((ISolidColorBrush)button.Background!).Color);
            Assert.Equal(new CornerRadius(12), button.CornerRadius);
        }
        finally
        {
            window.Close();
        }
    }

    // ===================== Typography roles =====================

    [Fact]
    public void Typography_DefaultsToTheBundledFonts_AndAThemeCanSetEachRole()
    {
        InstallTheme("type", Theme("""
            "typography": { "display": { "family": "Georgia", "size": 48, "weight": "Bold" }, "heading": { "family": "Georgia", "size": 26 }, "body": { "size": 15 }, "monospace": { "family": "Consolas" } }
            """));
        var service = CreateService();

        service.ApplyTheme("default");
        Assert.Contains("Bebas Neue", Res("PbDisplayFontFamily")!.ToString());
        Assert.Contains("Bebas Neue", Res("PbHeadingFontFamily")!.ToString());
        Assert.Equal(42d, Res("PbFontSizeHero"));
        Assert.Equal(22d, Res("PbFontSizeHeading"));
        Assert.Equal(FontWeight.Normal, Res("PbHeadingFontWeight"));
        Assert.Null(Res("PbFontSizeBody"));                                               // the app's own type scale (AppTokens) applies

        service.ApplyTheme("type");
        Assert.Contains("Georgia", Res("PbDisplayFontFamily")!.ToString());
        Assert.Equal(48d, Res("PbFontSizeHero"));
        Assert.Equal(26d, Res("PbFontSizeHeading"));
        Assert.Equal(FontWeight.Bold, Res("PbDisplayFontWeight"));
        Assert.Equal(15d, Res("PbFontSizeBody"));
        Assert.Contains("Consolas", Res("PbMonoFontFamily")!.ToString());

        service.ApplyTheme("default");
        Assert.Null(Res("PbFontSizeBody"));                                               // a theme's size does not outlive it
        Assert.Equal(42d, Res("PbFontSizeHero"));
    }

    [Fact]
    public void Typography_AFontThatIsNotInstalled_FallsBackToTheBundledOne_WithANote()
    {
        InstallTheme("type", Theme("""
            "typography": { "display": { "family": "No Such Font 4821" } }
            """));
        var service = CreateService();

        service.ApplyTheme("type");

        Assert.Contains("Bebas Neue", Res("PbDisplayFontFamily")!.ToString());
        Assert.Contains(service.GetActiveDiagnostics(), d => d.Code == ThemeDiagnosticCodes.FallbackUsed && d.PropertyPath == "typography.display.family");
    }

    [Fact]
    public void Typography_TheUsersOwnFontChoice_StillWinsForBodyText()
    {
        InstallTheme("type", Theme("""
            "typography": { "body": { "family": "Georgia" } }
            """));
        var service = CreateService();
        service.ApplyTheme("type");
        Assert.Contains("Georgia", Res("PbFontFamily")!.ToString());

        service.ApplyFont("Consolas");
        service.ApplyTheme("type");

        Assert.Contains("Consolas", Res("PbFontFamily")!.ToString());
    }

    // ===================== Materials =====================

    private const string MaterialTheme = """
        "materials": {
          "paper": { "type": "linearGradient", "angle": 180, "stops": [ { "at": 0, "color": { "ref": "palette.canvas" } }, { "at": 1, "color": "#1C2430" } ], "fallback": "#151B22" },
          "linen": { "type": "texture", "source": "assets/textures/linen.png", "opacity": 0.5, "fallback": "#202020" },
          "ghost": { "type": "image", "source": "assets/textures/missing.png", "fallback": "#303030" }
        },
        "surfaces": { "canvas": "paper", "raised": "linen", "overlay": "ghost", "navigation": "nope" },
        "components": { "button.secondary": { "background": "material:paper" } }
        """;

    [Fact]
    public void Materials_BecomeGradientAndImageBrushes_AndTheColourTokenStaysSolid()
    {
        InstallTheme("mat", Theme(MaterialTheme), ("assets/textures/linen.png", Png(8, 8)));
        var service = CreateService();

        service.ApplyTheme("mat");

        var canvas = Assert.IsType<LinearGradientBrush>(Res("PbBgBrush"));
        Assert.Equal(2, canvas.GradientStops.Count);
        Assert.Equal(C("#101418"), canvas.GradientStops[0].Color);                        // the stop followed its palette reference
        Assert.Equal(C("#101418"), Res("PbBgColor"));                                      // custom-drawn code still gets a plain colour

        var raised = Assert.IsType<ImageBrush>(Res("PbSurface2Brush"));
        Assert.Equal(TileMode.Tile, raised.TileMode);
        Assert.Equal(0.5, raised.Opacity);

        Assert.IsType<LinearGradientBrush>(Res("PbButtonSecondaryBackground"));           // a component can use a material too
    }

    [Fact]
    public void Materials_AMissingImageOrUnknownMaterial_FallsBackToASolidColour_WithANote()
    {
        InstallTheme("mat", Theme(MaterialTheme), ("assets/textures/linen.png", Png(8, 8)));
        var service = CreateService();

        service.ApplyTheme("mat");

        Assert.Equal(C("#303030"), BrushColor("PbSurface3Brush"));                         // the image is missing: its fallback
        Assert.IsAssignableFrom<ISolidColorBrush>(Res("PbChromeBrush"));                   // "nope" is not a material: the plain surface
        var notes = service.GetActiveDiagnostics();
        Assert.Contains(notes, d => d.PropertyPath == "materials.ghost" && d.FallbackUsed);
        Assert.Contains(notes, d => d.PropertyPath == "materials.nope" && d.FallbackUsed);
    }

    [Fact]
    public void Materials_ThePerformanceTier_UsesSolidFallbacksOnly()
    {
        InstallTheme("mat", Theme(MaterialTheme), ("assets/textures/linen.png", Png(8, 8)));
        var service = CreateService();
        service.ApplyTheme("mat");
        Assert.IsType<LinearGradientBrush>(Res("PbBgBrush"));

        service.SetEffectQuality(ThemeEffectQuality.Performance);

        Assert.Equal(C("#151B22"), BrushColor("PbBgBrush"));
        Assert.Equal(C("#202020"), ((ISolidColorBrush)Res("PbSurface2Brush")!).Color);

        service.SetEffectQuality(ThemeEffectQuality.High);
        Assert.IsType<LinearGradientBrush>(Res("PbBgBrush"));
    }

    [Fact]
    public void Materials_DoNotLingerWhenSwitchingToAThemeWithoutThem()
    {
        InstallTheme("mat", Theme(MaterialTheme), ("assets/textures/linen.png", Png(8, 8)));
        var service = CreateService();
        service.ApplyTheme("mat");

        service.ApplyTheme("default");

        Assert.Equal(C("#0A0B0D"), BrushColor("PbBgBrush"));
        Assert.Equal(C("#131519"), BrushColor("PbButtonSecondaryBackground"));
    }

    // ===================== Icons =====================

    [Theory]
    [InlineData("symbol:Rocket", ThemeIconKind.Symbol, "Rocket")]
    [InlineData("path:M0 0 L10 0 L10 10 Z", ThemeIconKind.Path, "M0 0 L10 0 L10 10 Z")]
    [InlineData("assets/icons/home.png", ThemeIconKind.Image, "assets/icons/home.png")]
    [InlineData("assets\\icons\\home.svg", ThemeIconKind.Path, "assets/icons/home.svg")]
    public void IconOverride_ClassifiesWhatAThemeWrote(string text, ThemeIconKind kind, string value)
    {
        var parsed = ThemeIconOverride.Parse(text);

        Assert.NotNull(parsed);
        Assert.Equal(kind, parsed!.Kind);
        Assert.Equal(value, parsed.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("symbol:not a name!")]
    [InlineData("../outside.png")]
    [InlineData("https://example.com/icon.png")]
    [InlineData("assets/icons/home.exe")]
    public void IconOverride_RefusesAnythingUnsafeOrUnusable(string text)
        => Assert.Null(ThemeIconOverride.Parse(text));

    [Fact]
    public void ThemedIcon_ShowsTheBuiltInGlyph_UntilAThemeReplacesIt_AndFallsBackWhenTheReplacementIsBroken()
    {
        InstallTheme("icons", Theme("""
            "icons": { "navigation.home": "symbol:Rocket", "navigation.library": "path:M0 0 L10 0 L10 10 Z", "navigation.books": "path:this is not path data",
                       "navigation.smart": "assets/icons/smart.png", "navigation.reading": "assets/icons/gone.png", "navigation.wanted": "assets/icons/wanted.svg" }
            """),
            ("assets/icons/smart.png", Png(16, 16)),
            ("assets/icons/wanted.svg", System.Text.Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script><path d="M0 0 L8 0 L8 8 Z"/></svg>""")));
        var service = CreateService();
        TestAppBuilder.EnsureInitialized();

        var names = new[] { "navigation.home", "navigation.library", "navigation.books", "navigation.smart", "navigation.reading", "navigation.wanted", "navigation.insights" };
        var icons = names.ToDictionary(n => n, n => new ThemedIcon { IconName = n });
        var panel = new StackPanel();
        panel.Children.AddRange(icons.Values);
        var window = new Window { Width = 300, Height = 300, Content = panel };
        window.Show();
        try
        {
            service.ApplyTheme("default");
            RunLayout(window);
            Assert.All(icons.Values, i => Assert.Equal(ThemeIconKind.Default, i.ShownKind));
            Assert.Equal(Symbol.Home, Assert.IsType<SymbolIcon>(icons["navigation.home"].Child).Symbol);

            service.ApplyTheme("icons");
            RunLayout(window);
            Assert.Equal(Symbol.Rocket, Assert.IsType<SymbolIcon>(icons["navigation.home"].Child).Symbol);
            Assert.IsType<Path>(icons["navigation.library"].Child);
            Assert.IsType<Image>(icons["navigation.smart"].Child);
            Assert.IsType<Path>(icons["navigation.wanted"].Child);                          // only the path outline of the SVG is used

            // Broken or missing replacements: the built-in glyph, never a blank.
            Assert.Equal(Symbol.BookOpen, Assert.IsType<SymbolIcon>(icons["navigation.books"].Child).Symbol);
            Assert.Equal(Symbol.Bookmark, Assert.IsType<SymbolIcon>(icons["navigation.reading"].Child).Symbol);
            Assert.Equal(Symbol.DataHistogram, Assert.IsType<SymbolIcon>(icons["navigation.insights"].Child).Symbol);   // not replaced at all

            service.ApplyTheme("default");
            RunLayout(window);
            Assert.Equal(Symbol.Home, Assert.IsType<SymbolIcon>(icons["navigation.home"].Child).Symbol);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ThemedIcon_KeepsItsFilledVariant_WhenTheScreenIsActive()
    {
        TestAppBuilder.EnsureInitialized();
        ThemeIconSource.Set(null, null);
        var icon = new ThemedIcon { IconName = "navigation.home", IconVariant = IconVariant.Filled };
        var window = new Window { Width = 100, Height = 100, Content = icon };
        window.Show();
        try
        {
            RunLayout(window);
            var symbol = Assert.IsType<SymbolIcon>(icon.Child);
            Assert.Equal(IconVariant.Filled, symbol.IconVariant);

            icon.IconVariant = IconVariant.Regular;
            Assert.Equal(IconVariant.Regular, symbol.IconVariant);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void EveryRegisteredIconName_HasARealBuiltInGlyph()
    {
        foreach (string name in ThemeIconRegistry.Names)
        {
            Assert.True(Enum.TryParse<Symbol>(ThemeIconRegistry.DefaultSymbol(name), out _), $"{name} falls back to '{ThemeIconRegistry.DefaultSymbol(name)}', which is not a glyph");
        }
    }

    // ===================== Scrollbar =====================

    [Fact]
    public void Scrollbar_MetricsReachTheFrameworkResource_AndAreRemovedAgain()
    {
        InstallTheme("thin", Theme("""
            "metrics": { "scrollbarWidth": 6, "scrollbarThumbOpacity": 0.5 }
            """));
        var service = CreateService();

        service.ApplyTheme("thin");
        Assert.Equal(6d, Res("ScrollBarSize"));
        Assert.Equal(0.5, Res("PbScrollbarThumbOpacity"));

        service.ApplyTheme("default");
        Assert.Null(Res("ScrollBarSize"));                                                  // the framework's own size is back
        Assert.Null(Res("PbScrollbarThumbOpacity"));
    }
}
