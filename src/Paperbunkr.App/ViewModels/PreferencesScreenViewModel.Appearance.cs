using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Appearance's sub-tabs, the grouped theme gallery with its inspector, and the live glow / density samples
/// (docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md). The settings themselves still live in
/// <c>PreferencesScreenViewModel.cs</c>; this file only holds what the redesign added.
/// </summary>
public partial class PreferencesScreenViewModel
{
    /// <summary>Tab keys of the Appearance section; the same strings are <see cref="PreferenceIndexEntry.SubTab"/> values.</summary>
    public static class AppearanceTabKeys
    {
        public const string Theme = "theme";
        public const string Covers = "covers";
        public const string Interface = "interface";
    }

    /// <summary>The Appearance section's tabs, in display order. In memory only: reopening Preferences starts on Theme.</summary>
    public SettingsTabs AppearanceTabs { get; } = new(new SettingsTabItem[]
    {
        new(AppearanceTabKeys.Theme, "Theme"),
        new(AppearanceTabKeys.Covers, "Covers & Detail"),
        new(AppearanceTabKeys.Interface, "Home & Interface"),
    });

    private static readonly string[] ThemeGroupOrder = { "Dark", "Light", "Accessibility" };

    /// <summary>The gallery: the loaded themes under their Dark / Light / Accessibility captions, empty groups left out.</summary>
    public ObservableCollection<ThemeGroup> ThemeGroups { get; } = new();

    /// <summary>The theme the inspector describes (always the active one). Null only before the first load.</summary>
    public ThemeSummary? ActiveThemeSummary { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLibraryThemePreview), nameof(ShowReaderThemePreview), nameof(ShowBookshelfThemePreview))]
    private string _activeThemePreviewTab = "Library";

    public bool ShowLibraryThemePreview => ActiveThemePreviewTab == "Library";
    public bool ShowReaderThemePreview => ActiveThemePreviewTab == "Reader";
    public bool ShowBookshelfThemePreview => ActiveThemePreviewTab == "Bookshelf";

    [RelayCommand]
    private void SelectThemePreviewTab(string? tab)
    {
        if (tab is "Library" or "Reader" or "Bookshelf") ActiveThemePreviewTab = tab;
    }

    /// <summary>The four glow tiers drawn as tiles (Off, Subtle, Normal, Vivid).</summary>
    public ObservableCollection<GlowTierSample> GlowTierSamples { get; } = new();

    /// <summary>The three density presets drawn as mini row stacks.</summary>
    public ObservableCollection<DensitySample> DensitySamples { get; } = new();

    private ThemeCustomizationViewModel? _themeCustomization;

    /// <summary>
    /// Appearance > Theme > Customize: the user's own values for all themes, this theme family or this variant (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 2).
    /// The "All themes" accent is the existing accent setting, so the two boxes stay one value.
    /// </summary>
    public ThemeCustomizationViewModel ThemeCustomization => _themeCustomization ??= new ThemeCustomizationViewModel(
        _themeService,
        () => string.IsNullOrWhiteSpace(AccentOverrideHexText) ? null : AccentOverrideHexText,
        value => AccentOverrideHexText = value ?? string.Empty);

    /// <summary>What the last install, export or import did ("Exported Daylight."), or why it could not. Empty until one is run.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThemeFileMessage))]
    private string _themeFileMessage = string.Empty;

    public bool HasThemeFileMessage => !string.IsNullOrEmpty(ThemeFileMessage);

    /// <summary>What the installer found in the last package that was installed: every warning, each naming the place in the file it is about.</summary>
    public ObservableCollection<string> InstallNotes { get; } = new();

    public bool HasInstallNotes => InstallNotes.Count > 0;

    private void RefreshInstallNotes()
    {
        InstallNotes.Clear();
        if (_themeService.LastInstallResult is { } result)
        {
            foreach (var finding in result.Diagnostics.Where(d => d.Severity != Theming.ThemeDiagnosticSeverity.Information))
            {
                InstallNotes.Add(string.IsNullOrEmpty(finding.PropertyPath) ? finding.Message : $"{finding.PropertyPath}: {finding.Message}");
            }
        }

        OnPropertyChanged(nameof(HasInstallNotes));
    }

    /// <summary>Saves the active theme as a .crpck, exactly as installed - a starting point for writing another one.</summary>
    [RelayCommand]
    private async Task ExportActiveTheme()
    {
        if (ActiveThemeSummary is not { } active)
        {
            return;
        }

        string? path = await _filePicker.PickSaveFileAsync("Export Theme", active.FamilyKey + ".crpck", "crpck", "Paperbunkr Theme");
        if (path is null)
        {
            return;
        }

        ThemeFileMessage = _themeService.ExportThemePackage(active.Key, path, out string? error)
            ? $"Exported {active.FamilyName}."
            : error ?? "The theme could not be exported.";
    }

    /// <summary>Saves everything changed under Customize as one JSON file.</summary>
    [RelayCommand]
    private async Task ExportThemeCustomizations()
    {
        string? path = await _filePicker.PickSaveFileAsync("Export My Customizations", "paperbunkr-theme-customizations.json", "json", "Theme customizations");
        if (path is null)
        {
            return;
        }

        ThemeFileMessage = _themeService.ExportCustomizations(path, out string? error) ? "Exported your customizations." : error ?? "Your customizations could not be exported.";
    }

    /// <summary>Replaces what is under Customize with a file written by the export above.</summary>
    [RelayCommand]
    private async Task ImportThemeCustomizations()
    {
        string? path = await _filePicker.PickOpenFileAsync("Import Customizations", "json", "Theme customizations");
        if (path is null)
        {
            return;
        }

        if (_themeService.ImportCustomizations(path, out string? error))
        {
            ThemeFileMessage = "Imported your customizations.";
            RefreshThemes();
        }
        else
        {
            ThemeFileMessage = error ?? "That file could not be imported.";
        }
    }

    /// <summary>The other variants of the active theme's family (Light / Dark / OLED ...), empty for a single-variant theme.</summary>
    public ObservableCollection<ThemeSummary> ActiveFamilyVariants { get; } = new();

    public bool HasActiveFamilyVariants => ActiveFamilyVariants.Count > 1;

    /// <summary>Rebuilds the gallery groups and the inspector's subject from <see cref="Themes"/>. Called by <c>RefreshThemes</c>.</summary>
    private void RefreshThemeGallery()
    {
        ThemeGroups.Clear();
        foreach (string title in ThemeGroupOrder)
        {
            var members = Themes.Where(t => string.Equals(t.Group, title, StringComparison.Ordinal)).ToList();
            if (members.Count > 0)
            {
                ThemeGroups.Add(new ThemeGroup(title, members));
            }
        }

        ActiveThemeSummary = Themes.FirstOrDefault(t => t.IsActive);
        OnPropertyChanged(nameof(ActiveThemeSummary));

        ActiveFamilyVariants.Clear();
        if (ActiveThemeSummary is { HasVariants: true } active)
        {
            foreach (var sibling in Themes.Where(t => string.Equals(t.FamilyKey, active.FamilyKey, StringComparison.OrdinalIgnoreCase)))
            {
                ActiveFamilyVariants.Add(sibling);
            }
        }

        OnPropertyChanged(nameof(HasActiveFamilyVariants));
        _themeCustomization?.Refresh();
        RefreshAppearanceSamples();
    }

    /// <summary>
    /// Rebuilds the glow-tier swatches from the active theme's glow colour and re-marks the active tier and density. Cheap, and safe to call
    /// before the settings have loaded (the active markers just stay off until they do).
    /// </summary>
    private void RefreshAppearanceSamples()
    {
        var glow = ResolveGlowColor();
        if (GlowTierSamples.Count == 0)
        {
            string[] labels = { "Off", "Subtle", "Normal", "Vivid" };
            for (int tier = ThemeService.GlowTierOff; tier <= ThemeService.GlowTierVivid; tier++)
            {
                GlowTierSamples.Add(new GlowTierSample(tier, labels[tier], ThemeService.BuildGlowRing(glow, tier)));
            }
        }
        else
        {
            foreach (var sample in GlowTierSamples)
            {
                sample.Shadow = ThemeService.BuildGlowRing(glow, sample.Tier);
            }
        }

        int activeTier = GlowTierFromText(GlowTierText);
        foreach (var sample in GlowTierSamples)
        {
            sample.IsActive = sample.Tier == activeTier;
        }

        if (DensitySamples.Count == 0)
        {
            for (int preset = DensityPresets.Compact; preset <= DensityPresets.Spacious; preset++)
            {
                // The real list-row padding is 6 / 10 / 14; the miniature uses 60% of it so three stacks fit beside each other.
                var real = DensityPresets.For(preset).ListRowPadding;
                DensitySamples.Add(new DensitySample(preset, DensityPresets.NameOf(preset), new Thickness(0, real.Top * 0.6, 0, real.Bottom * 0.6)));
            }
        }

        int activePreset = DensityPresets.FromName(DensityText);
        foreach (var sample in DensitySamples)
        {
            sample.IsActive = sample.Preset == activePreset;
        }
    }

    /// <summary>The live glow colour (written by <c>ThemeService</c> on every apply), falling back to the accent so a headless host without the app resources still gets a ring.</summary>
    private static Color ResolveGlowColor()
    {
        var resources = Application.Current?.Resources;
        if (resources is not null)
        {
            foreach (string key in new[] { "PbGlowColor", "PbAccentColor" })
            {
                if (resources.TryGetResource(key, null, out var value) && value is Color color)
                {
                    return color;
                }
            }
        }

        return Color.Parse("#99E0995A");
    }
}
