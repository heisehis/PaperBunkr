using Avalonia;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Personalization folded into the theme apply (docs/superpowers/specs/2026-11-06-personalization-design.md): text scale, heading font, card elevation and
/// focus-ring thickness are live, survive a theme switch, and their defaults reproduce the old resources. Same isolation as <see cref="ThemeGoldenResourceTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeServicePersonalizationTests : IDisposable
{
    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string? _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _root;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly ThemeService _service;
    private readonly IDisposable _tokens;

    public ThemeServicePersonalizationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_personal_{Guid.NewGuid():N}");
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        Directory.CreateDirectory(_root);
        string db = Path.Combine(_root, "personal.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={db}").Options;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Database.EnsureCreated();
        }

        PaperbunkrDbContext.DatabasePathOverride = db;
        ThemeService.ResetPersonalizationForTests();
        // The app's own type scale (AppTokens.axaml): a size the theme leaves unset falls back to it, as in the app.
        _tokens = FocusTestHarness.AppResources();
        _service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));
        _service.ApplyGlowTier(ThemeService.GlowTierNormal);
        _service.ApplyTheme("default");
    }

    public void Dispose()
    {
        // Leave the process-wide state and the live resources as the next test expects them.
        _service.ApplyPersonalization(s =>
        {
            s.FontScale = 100;
            s.HeadingFontFamily = null;
            s.CardElevation = 1;
            s.FocusRingThickness = 1;
            s.SidebarStyle = SidebarStyle.Standard;
        });
        ThemeService.ResetPersonalizationForTests();
        _tokens.Dispose();
        ThemePaths.InstalledDirectory = _originalInstalledDirectory;
        ThemePaths.ExtractedDirectory = _originalExtractedDirectory;
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static object? Resource(string key) =>
        Application.Current!.TryGetResource(key, null, out var value) ? value : null;

    private static double Size(string key) => Assert.IsType<double>(Resource(key));

    [Fact]
    public void Defaults_KeepTheTypeScaleAndTheOldCoverShadows()
    {
        Assert.Equal(13, Size("PbFontSizeBody"));
        Assert.Equal(22, Size("PbFontSizeHeading"));
        Assert.Equal(15, Size("PbFontSizeSection"));

        var poster = Assert.IsType<BoxShadows>(Resource("PbPosterShadow"));
        Assert.Equal(BoxShadows.Parse("0 6 18 -4 #80000000"), poster);
        Assert.Equal(BoxShadows.Parse("0 8 24 -12 #80000000"), Assert.IsType<BoxShadows>(Resource("PbCoverShadow")));
        Assert.Equal(BoxShadows.Parse("0 14 34 0 #66000000"), Assert.IsType<BoxShadows>(Resource("PbShelfShadow")));
        Assert.Equal(4, Assert.IsType<BoxShadows>(Resource("PbGlowRing"))[0].Spread);
    }

    [Fact]
    public void TextScale_ScalesTheTypeTokens_AndComesBackExactly()
    {
        _service.ApplyPersonalization(s => s.FontScale = 110);
        Assert.Equal(14.5, Size("PbFontSizeBody"));
        Assert.Equal(24, Size("PbFontSizeHeading"));
        Assert.Equal(16.5, Size("PbFontSizeSection"));
        Assert.Equal(11, Size("PbFontSizeMicro"));

        _service.ApplyPersonalization(s => s.FontScale = 100);
        Assert.Equal(13, Size("PbFontSizeBody"));
        Assert.Equal(22, Size("PbFontSizeHeading"));
        Assert.Equal(15, Size("PbFontSizeSection"));
        Assert.Equal(18, Size("PbFontSizeTitle"));
    }

    [Fact]
    public void TextScale_SurvivesAThemeSwitch()
    {
        _service.ApplyPersonalization(s => s.FontScale = 90);
        _service.ApplyTheme("daylight");

        Assert.Equal(11.5, Size("PbFontSizeBody"));
    }

    [Fact]
    public void HeadingFont_OverridesHeadingAndDisplay_AndNullRestoresTheTheme()
    {
        string themeHeading = Assert.IsType<FontFamily>(Resource("PbHeadingFontFamily")).Name;

        _service.ApplyPersonalization(s => s.HeadingFontFamily = "Consolas");
        Assert.Equal("Consolas", Assert.IsType<FontFamily>(Resource("PbHeadingFontFamily")).Name);
        Assert.Equal("Consolas", Assert.IsType<FontFamily>(Resource("PbDisplayFontFamily")).Name);

        _service.ApplyPersonalization(s => s.HeadingFontFamily = "  ");
        Assert.Equal(themeHeading, Assert.IsType<FontFamily>(Resource("PbHeadingFontFamily")).Name);
    }

    [Fact]
    public void CardElevation_FlatRemovesDepth_LiftedDeepensIt()
    {
        _service.ApplyPersonalization(s => s.CardElevation = 0);
        Assert.Equal(0, Assert.IsType<BoxShadows>(Resource("PbPosterShadow")).Count);
        Assert.Equal(1, Assert.IsType<BoxShadows>(Resource("PbPosterShadowHover")).Count); // the hover edge stays

        _service.ApplyPersonalization(s => s.CardElevation = 2);
        var lifted = Assert.IsType<BoxShadows>(Resource("PbPosterShadow"))[0];
        Assert.True(lifted.OffsetY > 6 && lifted.Blur > 18);
    }

    [Fact]
    public void FocusThickness_ScalesTheRing()
    {
        _service.ApplyPersonalization(s => s.FocusRingThickness = 2);
        Assert.Equal(6, Assert.IsType<BoxShadows>(Resource("PbGlowRing"))[0].Spread);

        _service.ApplyPersonalization(s => s.FocusRingThickness = 0);
        Assert.Equal(3, Assert.IsType<BoxShadows>(Resource("PbGlowRing"))[0].Spread);

        _service.ApplyGlowTier(ThemeService.GlowTierNormal); // the glow tier keeps the thickness
        Assert.Equal(3, Assert.IsType<BoxShadows>(Resource("PbGlowRing"))[0].Spread);
    }

    [Fact]
    public void SidebarStyle_IsPublished_AndInvalidValuesNormalise()
    {
        _service.ApplyPersonalization(s => s.SidebarStyle = SidebarStyle.Pill);
        Assert.True(PersonalizationRuntime.Instance.IsSidebarPill);

        _service.ApplyPersonalization(s =>
        {
            s.SidebarStyle = (SidebarStyle)42;
            s.FontScale = 300;
            s.CardElevation = 9;
        });
        var state = ThemeService.CurrentPersonalization;
        Assert.Equal(SidebarStyle.Standard, state.SidebarStyle);
        Assert.Equal(100, state.FontScale);
        Assert.Equal(2, state.CardElevation);
    }

    [Fact]
    public void AFreshStart_ReappliesThePersistedPersonalization()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.FontScale = 110;
            settings.CardElevation = 0;
            settings.FocusRingThickness = 2;
            settings.SidebarStyle = SidebarStyle.Tint;
            context.SaveChanges();
        }

        // A new process: the statics are back at their defaults until startup reads the settings.
        ThemeService.ResetPersonalizationForTests();
        var startup = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));
        startup.ApplyPersistedSettings();

        Assert.Equal(14.5, Size("PbFontSizeBody"));
        Assert.Equal(0, Assert.IsType<BoxShadows>(Resource("PbPosterShadow")).Count);
        Assert.Equal(6, Assert.IsType<BoxShadows>(Resource("PbGlowRing"))[0].Spread);
        Assert.True(PersonalizationRuntime.Instance.IsSidebarTint);
    }

    [Fact]
    public void AnAccentChange_KeepsThePersonalization()
    {
        _service.ApplyPersonalization(s => s.FontScale = 110);
        _service.SetAccentOverrideHex("#3366CC");

        Assert.Equal(14.5, Size("PbFontSizeBody"));
        _service.SetAccentOverrideHex(null);
    }

    [Fact]
    public void ApplyLook_ReappliesTheThemeOnce_AndSetsAllFiveValues()
    {
        _service.ApplyPersonalization(s => s.FontScale = 110);
        _service.SetAccentOverrideHex("#3366CC");
        int applied = 0;
        _service.ThemeApplied += () => applied++;

        var previous = _service.ApplyLook(LookPresets.Vivid.Values);

        Assert.Equal(1, applied);
        Assert.Equal(LookPresets.ThemeDefault, previous);
        Assert.Equal(LookPresets.Vivid.Values, _service.GetLook());
        Assert.Equal("22.4", _service.GetOverride(Paperbunkr.App.Theming.ThemeOverrideScope.Global, "metrics.radiusLg")); // 80 -> 14 + 0.6 * 14
        Assert.True(PersonalizationRuntime.Instance.IsSidebarPill);
        Assert.Equal(6, Assert.IsType<BoxShadows>(Resource("PbGlowRing"))[0].Spread);

        // Accent, fonts and text size are not a look's business.
        Assert.Equal(14.5, Size("PbFontSizeBody"));
        Assert.Equal("#3366CC", _service.GetAccentOverrideHex());

        _service.ApplyLook(LookPresets.ThemeDefault);
        Assert.Equal(LookPresets.ThemeDefault, _service.GetLook());
        Assert.Null(_service.GetOverride(Paperbunkr.App.Theming.ThemeOverrideScope.Global, "metrics.radius"));
        _service.SetAccentOverrideHex(null);
    }

    private string ExportedFile()
    {
        string path = Path.Combine(_root, $"look_{Guid.NewGuid():N}.json");
        Assert.True(_service.ExportCustomizations(path, out string? error), error);
        return path;
    }

    private AppSettings Settings()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.GetOrCreateAppSettings();
    }

    [Fact]
    public void ExportThenImport_RestoresTheWholeLook()
    {
        _service.SetAccentOverrideHex("#5FA889");
        _service.ApplyFont("Consolas");
        _service.ApplyLook(LookPresets.Vivid.Values);
        _service.ApplyPersonalization(s => { s.FontScale = 110; s.HeadingFontFamily = "Georgia"; });
        string file = ExportedFile();

        // Wipe it all.
        _service.SetAccentOverrideHex(null);
        _service.ApplyFont(null);
        _service.ApplyLook(LookPresets.ThemeDefault);
        _service.ApplyPersonalization(s => { s.FontScale = 100; s.HeadingFontFamily = null; });

        Assert.True(_service.ImportCustomizations(file, out string? error, out string? note), error);
        Assert.Null(note);

        var s = Settings();
        Assert.Equal("#5FA889", s.AccentOverrideHex);
        Assert.Equal("Consolas", s.SelectedFontFamily);
        Assert.Equal(110, s.FontScale);
        Assert.Equal("Georgia", s.HeadingFontFamily);
        Assert.Equal(LookPresets.Vivid.Values, _service.GetLook()); // corners come back through the Global overrides
        Assert.Equal(14.5, Size("PbFontSizeBody"));
    }

    [Fact]
    public void AnOldFile_WithoutTheBlock_LeavesPersonalizeAlone()
    {
        string file = ExportedFile();
        var root = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        root.Remove(PersonalizationSnapshot.BlockName);
        File.WriteAllText(file, root.ToJsonString());

        _service.ApplyPersonalization(s => s.FontScale = 90);
        Assert.True(_service.ImportCustomizations(file, out _, out string? note));

        Assert.Null(note);
        Assert.Equal(90, Settings().FontScale);
    }

    [Fact]
    public void ANewerBlock_IsSkippedWithANote_AndTheThemePartStillImports()
    {
        _service.SetOverride(Paperbunkr.App.Theming.ThemeOverrideScope.Global, "metrics.scrollbarWidth", "12");
        string file = ExportedFile();
        var root = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        root[PersonalizationSnapshot.BlockName]!["formatVersion"] = 99;
        root[PersonalizationSnapshot.BlockName]!["fontScale"] = 110;
        File.WriteAllText(file, root.ToJsonString());
        _service.SetOverride(Paperbunkr.App.Theming.ThemeOverrideScope.Global, "metrics.scrollbarWidth", null);

        Assert.True(_service.ImportCustomizations(file, out _, out string? note));

        Assert.NotNull(note);
        Assert.Equal(100, Settings().FontScale);
        Assert.Equal("12", _service.GetOverride(Paperbunkr.App.Theming.ThemeOverrideScope.Global, "metrics.scrollbarWidth"));
        _service.SetOverride(Paperbunkr.App.Theming.ThemeOverrideScope.Global, "metrics.scrollbarWidth", null);
    }

    [Fact]
    public void JunkValues_Normalise()
    {
        string file = ExportedFile();
        var root = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        root[PersonalizationSnapshot.BlockName] = new System.Text.Json.Nodes.JsonObject
        {
            ["formatVersion"] = 1, ["accent"] = "nope", ["fontScale"] = 300, ["sidebarStyle"] = "Wavy",
            ["cardElevation"] = "deep", ["focusRingThickness"] = -3, ["ringStrength"] = 42, ["someFutureKey"] = true,
        };
        File.WriteAllText(file, root.ToJsonString());

        Assert.True(_service.ImportCustomizations(file, out _, out _));

        var s = Settings();
        Assert.Null(s.AccentOverrideHex);
        Assert.Equal(100, s.FontScale);
        Assert.Equal(SidebarStyle.Standard, s.SidebarStyle);
        Assert.Equal(1, s.CardElevation);
        Assert.Equal(0, s.FocusRingThickness);
        Assert.Equal(ThemeService.GlowTierNormal, s.GlowTier);
    }

    [Fact]
    public void ANewFile_IsStillReadableByTheOldDeserializer()
    {
        var root = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ExportedFile()))!;
        Assert.NotNull(root[PersonalizationSnapshot.BlockName]);
        Assert.NotNull(Paperbunkr.App.Theming.Runtime.ThemeOverrideSerializer.Deserialize(root));
    }
}
