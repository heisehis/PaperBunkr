using Avalonia;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>The override file itself (design doc section 6.4, decision D2): versioned, atomic, set aside when unreadable. No application needed.</summary>
public class ThemeOverrideStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"pb_overrides_{Guid.NewGuid():N}");
    private readonly string _path;

    public ThemeOverrideStoreTests()
    {
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "theme-overrides.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void RoundTrips_EveryScope_TheFlags_AndLayoutPatches()
    {
        var overrides = new UserThemeOverrides { BoostContrast = true, EffectQuality = ThemeEffectQuality.Balanced };
        overrides.Global.Values["surface.canvas"] = "#101010";
        overrides.GetOrAdd(ThemeOverrideScope.Family, "org.test.paper", "dark").Values["action.primary"] = "#FF0000";
        overrides.GetOrAdd(ThemeOverrideScope.Variant, "org.test.paper", "dark").Values[ThemeTokenPaths.Component("button.primary", "hover", "background")] = "#00FF00";
        overrides.GetOrAddFamily("org.test.paper").LayoutPatches["Home"] = new Dictionary<string, System.Text.Json.Nodes.JsonObject>
        {
            ["home-search"] = new System.Text.Json.Nodes.JsonObject { ["visible"] = false },
        };

        new FileThemeOverrideStore(_path).Save(overrides);
        var loaded = new FileThemeOverrideStore(_path).Load();

        Assert.True(loaded.BoostContrast);
        Assert.Equal(ThemeEffectQuality.Balanced, loaded.EffectQuality);
        Assert.Equal("#101010", loaded.Global.Values["surface.canvas"]);
        Assert.Equal("#FF0000", loaded.Find(ThemeOverrideScope.Family, "org.test.paper", "dark")!.Values["action.primary"]);
        Assert.Equal("#00FF00", loaded.Find(ThemeOverrideScope.Variant, "org.test.paper", "dark")!.Values["component:button.primary:hover:background"]);
        Assert.False(loaded.FindFamily("org.test.paper")!.LayoutPatches["Home"]["home-search"]["visible"]!.GetValue<bool>());
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void AMissingFile_IsEmpty()
        => Assert.True(new FileThemeOverrideStore(_path).Load().Global.IsEmpty);

    [Fact]
    public void ACorruptFile_IsSetAside_NotOverwritten_AndDoesNotBreakLoading()
    {
        File.WriteAllText(_path, "{ this is not json");

        var loaded = new FileThemeOverrideStore(_path).Load();

        Assert.True(loaded.Global.IsEmpty);
        Assert.False(File.Exists(_path));
        Assert.Equal("{ this is not json", File.ReadAllText(_path + ".bad"));     // kept for recovery
    }

    [Fact]
    public void AFileFromANewerVersion_IsSetAside_RatherThanMisread()
    {
        File.WriteAllText(_path, """{ "version": 99, "global": { "surface.canvas": "#123456" } }""");

        var loaded = new FileThemeOverrideStore(_path).Load();

        Assert.True(loaded.Global.IsEmpty);
        Assert.True(File.Exists(_path + ".bad"));
    }

    [Fact]
    public void Saving_DropsEmptyFamiliesAndVariants()
    {
        var overrides = new UserThemeOverrides();
        overrides.GetOrAdd(ThemeOverrideScope.Variant, "org.test.paper", "dark");

        new FileThemeOverrideStore(_path).Save(overrides);

        Assert.Empty(new FileThemeOverrideStore(_path).Load().Families);
    }

    [Fact]
    public void HandWrittenNumbers_AreAccepted()
    {
        File.WriteAllText(_path, """{ "version": 1, "global": { "metrics.radius": 12 } }""");

        Assert.Equal("12", new FileThemeOverrideStore(_path).Load().Global.Values["metrics.radius"]);
    }
}

/// <summary>
/// Scoped user overrides applied live through <see cref="ThemeService"/>, and the customize panel's view-model
/// (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 2).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeServiceOverrideTests : IDisposable
{
    private const string PaperFamily = """
        { "schemaVersion": 2, "id": "org.test.paper", "name": "Paper",
          "base": { "palette": { "canvas": "#F7F3EC", "ink": "#2A2620", "accent": "#985A27" } },
          "variants": { "light": { "mode": "Light" }, "dark": { "mode": "Dark", "palette": { "canvas": "#12100D", "ink": "#EDE0C8" } } } }
        """;

    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pb_theme_ovr_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ThemeServiceOverrideTests()
    {
        Directory.CreateDirectory(_root);
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "o.db")}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        string folder = Path.Combine(ThemePaths.ExtractedDirectory, "paper");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "theme.json"), PaperFamily);
    }

    public void Dispose()
    {
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

    private ThemeService CreateService(IThemeOverrideStore? store = null, Func<DateTimeOffset>? now = null) => new(() => new PaperbunkrDbContext(_dbOptions), now, store);

    private static Color Resource(string key) => (Color)Application.Current!.Resources[key]!;

    private static Color C(string hex) => Color.Parse(hex);

    [Fact]
    public void AGlobalOverride_AppliesLive_SurvivesAThemeSwitch_AndResets()
    {
        var service = CreateService();
        service.ApplyTheme("default");

        Assert.True(service.SetOverride(ThemeOverrideScope.Global, "surface.canvas", "#202428"));
        Assert.Equal(C("#202428"), Resource("PbBgColor"));
        Assert.Equal(ThemeValueSource.UserGlobal, service.Current!.Token("surface.canvas")!.Source);

        service.ApplyTheme("cool_technical");
        Assert.Equal(C("#202428"), Resource("PbBgColor"));                        // "all themes" means all themes

        service.SetOverride(ThemeOverrideScope.Global, "surface.canvas", null);
        Assert.Equal(C("#0B0E14"), Resource("PbBgColor"));                        // cool_technical's own background is back
    }

    [Fact]
    public void AFamilyOverride_AppliesToEveryVariantOfThatFamily_AndToNoOtherTheme()
    {
        var service = CreateService();
        service.ApplyTheme("paper:dark");

        service.SetOverride(ThemeOverrideScope.Family, "action.primary", "#FF0000");
        Assert.Equal(C("#FF0000"), Resource("PbAccentColor"));

        service.ApplyTheme("paper:light");
        Assert.Equal(C("#FF0000"), Resource("PbAccentColor"));

        service.ApplyTheme("default");
        Assert.Equal(C("#C9803F"), Resource("PbAccentColor"));
    }

    [Fact]
    public void AVariantOverride_BeatsAFamilyOverride_WhichBeatsAGlobalOne()
    {
        var service = CreateService();
        service.ApplyTheme("paper:dark");

        service.SetOverride(ThemeOverrideScope.Global, "text.primary", "#111111");
        Assert.Equal(C("#111111"), Resource("PbTextColor"));

        service.SetOverride(ThemeOverrideScope.Family, "text.primary", "#222222");
        Assert.Equal(C("#222222"), Resource("PbTextColor"));

        service.SetOverride(ThemeOverrideScope.Variant, "text.primary", "#333333");
        Assert.Equal(C("#333333"), Resource("PbTextColor"));

        service.ApplyTheme("paper:light");
        Assert.Equal(C("#222222"), Resource("PbTextColor"));                      // the variant override was the dark variant's

        service.ResetOverrides(ThemeOverrideScope.Family);
        Assert.Equal(C("#111111"), Resource("PbTextColor"));
        Assert.Equal(1, service.CountOverrides(ThemeOverrideScope.Global));
        Assert.Equal(0, service.CountOverrides(ThemeOverrideScope.Family));
    }

    [Fact]
    public void AnInvalidValue_IsRefused_AndChangesNothing()
    {
        var service = CreateService();
        service.ApplyTheme("default");

        Assert.False(service.SetOverride(ThemeOverrideScope.Global, "surface.canvas", "bluish"));
        Assert.False(service.SetOverride(ThemeOverrideScope.Global, "metrics.radius", "#FF0000"));

        Assert.Equal(C("#0A0B0D"), Resource("PbBgColor"));
        Assert.Equal(0, service.CountOverrides(ThemeOverrideScope.Global));
    }

    [Fact]
    public void ANumberOverride_ChangesTheCornerRadius()
    {
        var service = CreateService();
        service.ApplyTheme("default");

        Assert.True(service.SetOverride(ThemeOverrideScope.Global, "metrics.radius", "12"));

        Assert.Equal(new CornerRadius(12), Application.Current!.Resources["PbRadius"]);
    }

    [Fact]
    public void Overrides_PersistAcrossRestarts_ThroughTheFileStore()
    {
        string path = Path.Combine(_root, "theme-overrides.json");
        var first = CreateService(new FileThemeOverrideStore(path));
        first.ApplyTheme("default");
        first.SetOverride(ThemeOverrideScope.Global, "surface.canvas", "#202428");

        var second = CreateService(new FileThemeOverrideStore(path));
        second.ApplyPersistedSettings();

        Assert.Equal(C("#202428"), Resource("PbBgColor"));
        Assert.Equal("#202428", second.GetOverride(ThemeOverrideScope.Global, "surface.canvas"));
    }

    [Fact]
    public void OverridesChanged_AndThemeApplied_AreRaised_SoBakedVisualsCanFollow()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        int overridesChanged = 0, themeApplied = 0;
        service.OverridesChanged += () => overridesChanged++;
        service.ThemeApplied += () => themeApplied++;

        service.SetOverride(ThemeOverrideScope.Global, "surface.canvas", "#202428");

        Assert.Equal(1, overridesChanged);
        Assert.Equal(1, themeApplied);
    }

    [Fact]
    public void BoostContrast_IsOffByDefault_AndRaisesTextOnlyWhenTurnedOn()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        service.SetOverride(ThemeOverrideScope.Global, "text.primary", "#4A4A4A");
        Assert.False(service.GetBoostContrast());
        Assert.Equal(C("#4A4A4A"), Resource("PbTextColor"));
        Assert.Contains(service.GetActiveDiagnostics(), d => d.Code == ThemeDiagnosticCodes.LowContrast && d.PropertyPath == "text.primary");

        service.SetBoostContrast(true);

        Assert.True(ThemeColorMath.ContrastRatio(Resource("PbTextColor"), C("#0A0B0D")) >= 4.5);
        Assert.Equal(ThemeValueSource.Accessibility, service.Current!.Token("text.primary")!.Source);

        service.SetBoostContrast(false);
        Assert.Equal(C("#4A4A4A"), Resource("PbTextColor"));
    }

    [Fact]
    public void Danger_IsNowThemeAware_UnchangedOnDark_DeepenedOnLight()
    {
        var service = CreateService();

        service.ApplyTheme("default");
        Assert.Equal(C("#D96C6C"), Resource("PbDangerColor"));
        Assert.Equal(C("#2ED96C6C"), Resource("PbDangerSoftColor"));
        Assert.Equal(C("#2E5FA889"), Resource("PbSuccessSoftColor"));

        service.ApplyTheme("daylight");
        var danger = Resource("PbDangerColor");
        Assert.NotEqual(C("#D96C6C"), danger);
        Assert.True(ThemeColorMath.ContrastRatio(danger, C("#F7F3EC")) >= 4.5);
        Assert.Equal(ThemeColorMath.WithAlpha(C("#3E7A56"), 0x2E), Resource("PbSuccessSoftColor"));    // follows daylight's own green now
    }

    [Fact]
    public void ScheduledAutoSwitch_PrefersAVariantOfTheActiveFamily_OverTheRememberedTheme()
    {
        var ninePm = new DateTimeOffset(2026, 1, 1, 21, 0, 0, TimeSpan.Zero);
        var service = CreateService(now: () => ninePm);
        service.ApplyTheme("cool_technical");                                     // the remembered dark theme
        service.ApplyTheme("paper:light");                                        // active: a family that has its own dark variant

        service.SetThemeAutoMode(Paperbunkr.Data.Entities.ThemeAutoMode.Scheduled);

        Assert.Equal("paper:dark", service.GetActiveThemeKey());
        service.SetThemeAutoMode(Paperbunkr.Data.Entities.ThemeAutoMode.Off);
    }

    // ===================== Customize panel =====================

    private ThemeCustomizationViewModel CreatePanel(ThemeService service, out Func<string?> accent)
    {
        string? stored = null;
        accent = () => stored;
        return new ThemeCustomizationViewModel(service, () => stored, value =>
        {
            stored = value;
            service.SetAccentOverrideHex(value);
        });
    }

    [Fact]
    public void Panel_ShowsTheLiveValueAndItsSource_ForEachRow()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        var panel = CreatePanel(service, out _);

        var background = panel.Rows.Single(r => r.Path == "surface.canvas");
        Assert.Equal("#0A0B0D", background.ResolvedText);
        Assert.Equal("From the theme", background.SourceText);
        Assert.False(background.IsOverridden);
        Assert.Equal(string.Empty, background.ValueText);
        Assert.Equal("7", panel.Rows.Single(r => r.Path == "metrics.radius").ResolvedText);
        Assert.Equal("Worked out from the theme", panel.Rows.Single(r => r.Path == "status.danger").SourceText);
    }

    [Fact]
    public void Panel_TypingACompleteColour_AppliesIt_AndAHalfTypedOneDoesNot()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        var panel = CreatePanel(service, out _);
        var background = panel.Rows.Single(r => r.Path == "surface.canvas");

        background.ValueText = "#2024";                                           // parses as shorthand, but is somebody mid-typing
        Assert.True(background.IsInvalid);
        Assert.Equal(C("#0A0B0D"), Resource("PbBgColor"));

        background.ValueText = "#202428";
        Assert.False(background.IsInvalid);
        Assert.Equal(C("#202428"), Resource("PbBgColor"));
        Assert.True(background.IsOverridden);
        Assert.Equal("Your setting for all themes", background.SourceText);
        Assert.True(panel.HasOverridesInScope);
        Assert.Equal("1 value changed.", panel.ScopeSummary);

        background.ResetCommand.Execute(null);
        Assert.Equal(C("#0A0B0D"), Resource("PbBgColor"));
        Assert.False(panel.HasOverridesInScope);
    }

    [Fact]
    public void Panel_ScopeDecidesWhichLayerIsEdited()
    {
        var service = CreateService();
        service.ApplyTheme("paper:dark");
        var panel = CreatePanel(service, out _);

        panel.ScopeText = ThemeCustomizationViewModel.ScopeThisVariant;
        panel.Rows.Single(r => r.Path == "text.primary").ValueText = "#ABCDEF";

        Assert.Equal("#ABCDEF", service.GetOverride(ThemeOverrideScope.Variant, "text.primary"));
        Assert.Null(service.GetOverride(ThemeOverrideScope.Global, "text.primary"));

        panel.ScopeText = ThemeCustomizationViewModel.ScopeAllThemes;
        var row = panel.Rows.Single(r => r.Path == "text.primary");
        Assert.Equal(string.Empty, row.ValueText);                                 // nothing set at this scope...
        Assert.Equal("#ABCDEF", row.ResolvedText);                                 // ...but the live value and where it comes from are shown
        Assert.Equal("Your setting for this variant", row.SourceText);
    }

    [Fact]
    public void Panel_TheAllThemesAccent_IsTheExistingAccentSetting()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        var panel = CreatePanel(service, out var accent);

        panel.Rows.Single(r => r.Path == "action.primary").ValueText = "#3366CC";

        Assert.Equal("#3366CC", accent());
        Assert.Equal("#3366CC", service.GetAccentOverrideHex());
        Assert.Equal(0, service.CountOverrides(ThemeOverrideScope.Global));        // not stored a second time in the override file
        Assert.Equal(C("#3366CC"), Resource("PbAccentColor"));

        panel.ResetScopeCommand.Execute(null);
        Assert.Null(service.GetAccentOverrideHex());
        Assert.Equal(C("#C9803F"), Resource("PbAccentColor"));
    }

    [Fact]
    public void Panel_ListsContrastNotes_ForTheThemeAsShown()
    {
        var service = CreateService();
        service.ApplyTheme("windows_11");
        var panel = CreatePanel(service, out _);

        Assert.True(panel.HasNotes);
        Assert.Contains(panel.Notes, n => n.Contains("4.08:1"));
    }
}
