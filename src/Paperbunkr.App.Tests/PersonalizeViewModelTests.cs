using Avalonia;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Appearance > Personalize (docs/superpowers/specs/2026-11-06-personalization-design.md), against a real ThemeService on an isolated database and theme folder.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PersonalizeViewModelTests : IDisposable
{
    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string? _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _root;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly ThemeService _service;
    private readonly IDisposable _tokens;
    private string? _accent;

    public PersonalizeViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_personalize_vm_{Guid.NewGuid():N}");
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        Directory.CreateDirectory(_root);
        string db = Path.Combine(_root, "vm.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={db}").Options;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Database.EnsureCreated();
        }

        PaperbunkrDbContext.DatabasePathOverride = db;
        ThemeService.ResetPersonalizationForTests();
        _tokens = FocusTestHarness.AppResources();
        _service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));
        _service.ApplyGlowTier(ThemeService.GlowTierNormal);
        _service.ApplyTheme("default");
    }

    public void Dispose()
    {
        _service.ApplyPersonalization(s =>
        {
            s.FontScale = 100;
            s.HeadingFontFamily = null;
            s.CardElevation = 1;
            s.FocusRingThickness = 1;
            s.SidebarStyle = SidebarStyle.Standard;
        });
        _service.ResetOverrides(ThemeOverrideScope.Global);
        ThemeService.ResetPersonalizationForTests();
        _tokens.Dispose();
        ThemePaths.InstalledDirectory = _originalInstalledDirectory;
        ThemePaths.ExtractedDirectory = _originalExtractedDirectory;
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PersonalizeViewModel NewVm() =>
        new(_service, () => _accent, value => { _accent = value; _service.SetAccentOverrideHex(value); }, new[] { "Consolas", "Georgia" }, run => run());

    private AppSettings Settings()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.GetOrCreateAppSettings();
    }

    private static object? Resource(string key) => Application.Current!.TryGetResource(key, null, out var value) ? value : null;

    [Fact]
    public void Loads_TheDefaults()
    {
        var vm = NewVm();
        Assert.Equal("Soft", vm.ElevationText);
        Assert.Equal("100%", vm.FontScaleText);
        Assert.Equal(PersonalizeViewModel.ThemeDefaultFont, vm.HeadingFontText);
        Assert.Equal("Standard", vm.SidebarStyleText);
        Assert.Equal("Normal", vm.FocusRingText);
        Assert.Equal(50, vm.Corners);
        Assert.Equal("Theme default", vm.CornersText);
        Assert.False(vm.HasAccentOverride);
        Assert.Equal(PersonalizeViewModel.ThemeDefaultFont, vm.HeadingFontOptions[0]);
    }

    [Fact]
    public void EachSetting_PersistsAndAppliesLive()
    {
        var vm = NewVm();

        vm.FontScaleText = "110%";
        vm.ElevationText = "Flat";
        vm.HeadingFontText = "Georgia";
        vm.SidebarStyleText = "Pill";
        vm.FocusRingText = "Thick";

        var s = Settings();
        Assert.Equal(110, s.FontScale);
        Assert.Equal(0, s.CardElevation);
        Assert.Equal("Georgia", s.HeadingFontFamily);
        Assert.Equal(SidebarStyle.Pill, s.SidebarStyle);
        Assert.Equal(2, s.FocusRingThickness);

        Assert.Equal(14.5, Resource("PbFontSizeBody"));
        Assert.Equal(0, Assert.IsType<BoxShadows>(Resource("PbPosterShadow")).Count);
        Assert.True(PersonalizationRuntime.Instance.IsSidebarPill);

        vm.HeadingFontText = PersonalizeViewModel.ThemeDefaultFont;
        Assert.Null(Settings().HeadingFontFamily);
    }

    [Fact]
    public void Corners_WriteTheThreeGlobalOverrides_AndTheMiddleClearsThem()
    {
        var vm = NewVm();

        vm.Corners = 100;
        Assert.Equal("14", _service.GetOverride(ThemeOverrideScope.Global, "metrics.radius"));
        Assert.Equal("10", _service.GetOverride(ThemeOverrideScope.Global, "metrics.radiusSm"));
        Assert.Equal("28", _service.GetOverride(ThemeOverrideScope.Global, "metrics.radiusLg"));
        Assert.Equal("Round", vm.CornersText);

        vm.Corners = 50;
        Assert.Null(_service.GetOverride(ThemeOverrideScope.Global, "metrics.radius"));
        Assert.Equal("Theme default", vm.CornersText);
    }

    [Fact]
    public void Corners_ShowCustom_WhenCustomizeEditsARadiusOffTheCurve()
    {
        var vm = NewVm();

        _service.SetOverride(ThemeOverrideScope.Global, "metrics.radiusLg", "3"); // OverridesChanged -> the slider re-reads

        Assert.True(vm.IsCustomCorners);
        Assert.Equal("Custom", vm.CornersText);

        vm.ResetCornersCommand.Execute(null);
        Assert.False(vm.IsCustomCorners);
        Assert.Null(_service.GetOverride(ThemeOverrideScope.Global, "metrics.radiusLg"));
    }

    [Fact]
    public void Accent_SwatchCustomAndReset_GoThroughTheSharedSetting()
    {
        var vm = NewVm();

        vm.SetAccentCommand.Execute("#5fa889");
        Assert.Equal("#5FA889", _accent);
        Assert.Equal("#5FA889", Settings().AccentOverrideHex);
        Assert.True(vm.SwatchItems.Single(s => s.Hex == "#5FA889").IsSelected);
        Assert.False(vm.IsCustomAccent);

        vm.CustomAccentHex = "123abc";
        vm.ApplyCustomAccentCommand.Execute(null);
        Assert.Equal("#123ABC", _accent);
        Assert.True(vm.IsCustomAccent);

        vm.CustomAccentHex = "nope";
        vm.ApplyCustomAccentCommand.Execute(null);
        Assert.NotNull(vm.CustomAccentError);
        Assert.Equal("#123ABC", _accent);

        vm.ResetAccentCommand.Execute(null);
        Assert.Null(_accent);
        Assert.Null(Settings().AccentOverrideHex);
    }

    [Fact]
    public void ContrastHint_FollowsTheSurfaceSeam()
    {
        var vm = NewVm();
        vm.SurfaceColorProvider = () => Color.FromRgb(0xF5, 0xF1, 0xEA);

        vm.SetAccentCommand.Execute("#F0ECE0");
        Assert.True(vm.HasAccentContrastWarning);

        vm.SetAccentCommand.Execute("#303030");
        Assert.False(vm.HasAccentContrastWarning);
    }

    [Fact]
    public void Reset_RestoresPersonalizeOnly_AndLeavesOtherOverridesAndSettings()
    {
        var vm = NewVm();
        _service.ApplyDensity(DensityPresets.Spacious);
        _service.SetOverride(ThemeOverrideScope.Global, "metrics.scrollbarWidth", "12");
        vm.SetAccentCommand.Execute("#5FA889");
        vm.Corners = 0;
        vm.FontScaleText = "90%";
        vm.SidebarStyleText = "Tint";

        vm.ResetCommand.Execute(null);

        var s = Settings();
        Assert.Null(s.AccentOverrideHex);
        Assert.Equal(100, s.FontScale);
        Assert.Equal(SidebarStyle.Standard, s.SidebarStyle);
        Assert.Null(_service.GetOverride(ThemeOverrideScope.Global, "metrics.radius"));
        Assert.Equal(50, vm.Corners);
        Assert.Equal("100%", vm.FontScaleText);

        Assert.Equal("12", _service.GetOverride(ThemeOverrideScope.Global, "metrics.scrollbarWidth")); // not Personalize's
        Assert.Equal(DensityPresets.Spacious, _service.GetDensityPreset());
        _service.ApplyDensity(DensityPresets.Comfortable);
    }

    [Fact]
    public void BodyFontAndRingStrength_GoThroughTheLinks_AndResetRestoresThem()
    {
        string? body = "Georgia";
        string ring = "Vivid";
        var samples = new List<Paperbunkr.App.Models.GlowTierSample>();
        var links = new PersonalizeLinks(() => body, v => body = v, () => ring, v => ring = v, new[] { "Off", "Subtle", "Normal", "Vivid" }, samples);
        var vm = new PersonalizeViewModel(_service, () => _accent, v => _accent = v, new[] { "System Default", "Consolas", "Georgia" }, run => run(), links);

        Assert.True(vm.HasLinks);
        Assert.Equal("Georgia", vm.BodyFontText);
        Assert.Equal("Vivid", vm.RingStrengthText);
        Assert.Equal(new[] { "System Default", "Consolas", "Georgia" }, vm.BodyFontOptions);
        Assert.DoesNotContain("System Default", vm.HeadingFontOptions); // not a heading font
        Assert.Same(samples, vm.RingSamples);

        vm.BodyFontText = "Consolas";
        vm.RingStrengthText = "Subtle";
        Assert.Equal("Consolas", body);
        Assert.Equal("Subtle", ring);

        vm.ResetCommand.Execute(null);
        Assert.Equal("System Default", body);
        Assert.Equal("Normal", ring);
        Assert.Equal("System Default", vm.BodyFontText);
        Assert.Equal("Normal", vm.RingStrengthText);
    }

    [Fact]
    public void WithoutLinks_TheTwoMovedRowsAreHidden()
    {
        var vm = NewVm();
        Assert.False(vm.HasLinks);
        Assert.Empty(vm.RingStrengthOptions);
    }

    [Fact]
    public void Customize_HidesTheAccentAndCornerRows_OnlyForAllThemes()
    {
        var customize = new ThemeCustomizationViewModel(_service, () => _accent, v => _accent = v);
        var owned = customize.Rows.Where(r => r.PersonalizeOwns).Select(r => r.Path).ToList();
        Assert.Equal(new[] { "action.primary", "metrics.radius" }, owned);

        Assert.Equal(ThemeCustomizationViewModel.ScopeAllThemes, customize.ScopeText);
        Assert.True(customize.ShowPersonalizeNote);
        Assert.All(customize.Rows, r => Assert.Equal(!r.PersonalizeOwns, r.IsShown));

        customize.ScopeText = ThemeCustomizationViewModel.ScopeThisTheme;
        Assert.False(customize.ShowPersonalizeNote);
        Assert.All(customize.Rows, r => Assert.True(r.IsShown));
    }

    private PersonalizeViewModel NewLinkedVm()
    {
        string tiers = "Off,Subtle,Normal,Vivid";
        string ring = "Normal";
        var options = tiers.Split(',');
        var links = new PersonalizeLinks(() => "System Default", _ => { }, () => ring, v => { ring = v; _service.ApplyGlowTier(Array.IndexOf(options, v)); },
            options, Array.Empty<Paperbunkr.App.Models.GlowTierSample>(), () => ring = options[_service.GetGlowTier()]);
        return new PersonalizeViewModel(_service, () => _accent, v => { _accent = v; _service.SetAccentOverrideHex(v); }, new[] { "System Default", "Georgia" }, run => run(), links);
    }

    [Fact]
    public void Presets_ApplyTheirFiveValues_SelectTheirCard_AndLeaveAccentFontsAndTextSizeAlone()
    {
        var vm = NewLinkedVm();
        vm.FontScaleText = "110%";
        vm.SetAccentCommand.Execute("#5FA889");
        Assert.Equal("Custom", vm.LookLabel); // the theme's own look is not a preset

        vm.ApplyPresetCommand.Execute("Quiet");

        Assert.Equal("Quiet", vm.LookLabel);
        Assert.Single(vm.PresetCards, c => c.IsSelected);
        Assert.True(vm.PresetCards.Single(c => c.Name == "Quiet").IsSelected);
        Assert.Equal(35, vm.Corners);
        Assert.Equal("Flat", vm.ElevationText);
        Assert.Equal("Tint", vm.SidebarStyleText);
        Assert.Equal("Thin", vm.FocusRingText);
        Assert.Equal("Subtle", vm.RingStrengthText); // the mirrored value was reloaded
        Assert.Equal("110%", vm.FontScaleText);
        Assert.Equal("#5FA889", Settings().AccentOverrideHex);
        Assert.True(vm.CanUndoPreset);
    }

    [Fact]
    public void Undo_RestoresTheLookBefore_IncludingCustomRadii()
    {
        var vm = NewLinkedVm();
        _service.SetOverride(ThemeOverrideScope.Global, "metrics.radiusLg", "3"); // Customize edit, off the curve
        Assert.True(vm.IsCustomCorners);

        vm.ApplyPresetCommand.Execute("Vivid");
        Assert.Equal("Vivid", vm.LookLabel);

        vm.UndoPresetCommand.Execute(null);

        Assert.False(vm.CanUndoPreset);
        Assert.True(vm.IsCustomCorners);
        Assert.Equal("3", _service.GetOverride(ThemeOverrideScope.Global, "metrics.radiusLg"));
        Assert.Null(_service.GetOverride(ThemeOverrideScope.Global, "metrics.radius"));
        Assert.Equal("Soft", vm.ElevationText);
        Assert.Equal("Standard", vm.SidebarStyleText);
        Assert.Equal("Normal", vm.RingStrengthText);
        Assert.Equal("Custom", vm.LookLabel);
    }

    [Fact]
    public void AManualChange_GivesCustom_AndDropsUndo_AndResetDropsItToo()
    {
        var vm = NewLinkedVm();
        vm.ApplyPresetCommand.Execute("Collector");
        Assert.Equal("Collector", vm.LookLabel);

        vm.SidebarStyleText = "Tint";
        Assert.Equal("Custom", vm.LookLabel);
        Assert.False(vm.CanUndoPreset);
        Assert.DoesNotContain(vm.PresetCards, c => c.IsSelected);

        vm.ApplyPresetCommand.Execute("Collector");
        Assert.True(vm.CanUndoPreset);
        vm.ResetCommand.Execute(null);
        Assert.False(vm.CanUndoPreset);
        Assert.Equal("Custom", vm.LookLabel);
    }

    [Fact]
    public void ChangingTextSize_KeepsThePresetAndItsUndo()
    {
        var vm = NewLinkedVm();
        vm.ApplyPresetCommand.Execute("Vivid");
        vm.FontScaleText = "90%";
        Assert.Equal("Vivid", vm.LookLabel);
        Assert.True(vm.CanUndoPreset);
    }
}
