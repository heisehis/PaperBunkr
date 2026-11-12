using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
        public const string Personalize = "personalize";
        public const string Covers = "covers";
        public const string Interface = "interface";
    }

    /// <summary>The Appearance section's tabs, in display order. In memory only: reopening Preferences starts on Theme.</summary>
    public SettingsTabs AppearanceTabs { get; } = new(new SettingsTabItem[]
    {
        new(AppearanceTabKeys.Theme, "Theme"),
        // Personalize and Customize have their own tab (docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md Q2).
        new(AppearanceTabKeys.Personalize, "Personalize"),
        new(AppearanceTabKeys.Covers, "Covers & Detail"),
        new(AppearanceTabKeys.Interface, "Home & Interface"),
    });

    /// <summary>The gallery switch's Installed key; the others are the <see cref="ThemeSummary.Group"/> names.</summary>
    public const string InstalledGallerySegment = "Installed";

    private static readonly string[] GallerySegmentOrder = { "Dark", "Light", "Accessibility", InstalledGallerySegment };

    /// <summary>
    /// The gallery's group switch (docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md Q3): Dark / Light / Accessibility / Installed with counts.
    /// Counts include installed themes; OLED variants are not counted (True black reaches them).
    /// </summary>
    public ObservableCollection<ThemeGallerySegment> ThemeGallerySegments { get; } = new(GallerySegmentOrder.Select(k => new ThemeGallerySegment(k, k)));

    /// <summary>The tiles of the selected segment: built-in themes first, then installed ones (the Installed segment shows only those).</summary>
    public ObservableCollection<ThemeSummary> VisibleThemes { get; } = new();

    /// <summary>The selected segment key. Follows the active theme's group until the user picks a segment, then stays for the session.</summary>
    public string SelectedGallerySegment { get; private set; } = "Dark";

    private bool _gallerySegmentChosen;

    [RelayCommand]
    private void SelectGallerySegment(string? key)
    {
        if (key is null || !GallerySegmentOrder.Contains(key, StringComparer.Ordinal))
        {
            return;
        }

        _gallerySegmentChosen = true;
        SetGallerySegment(key);
    }

    private void SetGallerySegment(string key)
    {
        SelectedGallerySegment = key;
        OnPropertyChanged(nameof(SelectedGallerySegment));
        foreach (var segment in ThemeGallerySegments)
        {
            segment.IsSelected = string.Equals(segment.Key, key, StringComparison.Ordinal);
        }

        SyncVisibleThemes();
    }

    private static bool InGallerySegment(ThemeSummary theme, string key) => string.Equals(key, InstalledGallerySegment, StringComparison.Ordinal)
        ? theme.IsInstalled
        : string.Equals(theme.Group, key, StringComparison.Ordinal);

    private void SyncVisibleThemes()
    {
        var shown = Themes.Where(t => !t.IsGalleryHidden).ToList();
        foreach (var segment in ThemeGallerySegments)
        {
            segment.Count = shown.Count(t => InGallerySegment(t, segment.Key));
        }

        // OrderBy is stable: built-ins keep the catalog order, installed themes follow.
        SyncCollection(VisibleThemes, shown.Where(t => InGallerySegment(t, SelectedGallerySegment)).OrderBy(t => t.IsInstalled).ToList());
    }

    /// <summary>
    /// Makes <paramref name="target"/> hold <paramref name="desired"/> (by reference) with the fewest removes/inserts, so items that stay keep their containers -
    /// a tile or variant button that was just clicked is never detached while its Click is still routing.
    /// </summary>
    internal static void SyncCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Any(d => ReferenceEquals(d, target[i])))
            {
                target.RemoveAt(i);
            }
        }

        for (int i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i]))
            {
                continue;
            }

            int existing = -1;
            for (int j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], desired[i]))
                {
                    existing = j;
                    break;
                }
            }

            if (existing >= 0)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, desired[i]);
            }
        }
    }

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

    private PersonalizeViewModel? _personalize;

    /// <summary>
    /// Appearance > Personalize (docs/superpowers/specs/2026-11-06-personalization-design.md). Its accent goes through <see cref="AccentOverrideHexText"/>, so it is
    /// the same value Customize's "All themes" accent shows.
    /// </summary>
    public PersonalizeViewModel Personalize => _personalize ??= new PersonalizeViewModel(
        _themeService,
        () => string.IsNullOrWhiteSpace(AccentOverrideHexText) ? null : AccentOverrideHexText,
        value => AccentOverrideHexText = value ?? string.Empty,
        FontFamilies,
        links: new PersonalizeLinks(
            () => SelectedFontFamily,
            font => SelectedFontFamily = font,
            () => GlowTierText,
            tier => GlowTierText = tier,
            GlowTierOptionsList,
            GlowTierSamples,
            // A preset writes the glow tier directly: re-read it without applying, or this screen would write the old tier back.
            () =>
            {
                _suppressThemeOptionsApply = true;
                GlowTierText = GlowTierToText(_themeService.GetGlowTier());
                _suppressThemeOptionsApply = false;
            },
            ExportThemeCustomizationsCommand,
            ImportThemeCustomizationsCommand));

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
        string? path = await _filePicker.PickSaveFileAsync("Export My Look", "paperbunkr-look.json", "json", "Paperbunkr look");
        if (path is null)
        {
            return;
        }

        ThemeFileMessage = _themeService.ExportCustomizations(path, out string? error) ? "Exported your look." : error ?? "Your look could not be exported.";
    }

    /// <summary>Replaces what is under Customize with a file written by the export above.</summary>
    [RelayCommand]
    private async Task ImportThemeCustomizations()
    {
        string? path = await _filePicker.PickOpenFileAsync("Import a Look", "json", "Paperbunkr look");
        if (path is null)
        {
            return;
        }

        if (_themeService.ImportCustomizations(path, out string? error, out string? note))
        {
            ThemeFileMessage = note is null ? "Imported your look." : "Imported your look. " + note;
            ReloadLookMirrors();
            RefreshThemes();
        }
        else
        {
            ThemeFileMessage = error ?? "That file could not be imported.";
        }
    }


    /// <summary>
    /// After an import (or anything else that set them in the service), re-read the values this screen mirrors - the accent, the body font and the glow tier - without
    /// applying them again, then let Customize and Personalize re-read too.
    /// </summary>
    private void ReloadLookMirrors()
    {
        _suppressThemeOptionsApply = true;
        AccentOverrideHexText = _themeService.GetAccentOverrideHex() ?? string.Empty;
        GlowTierText = GlowTierToText(_themeService.GetGlowTier());
        _suppressThemeOptionsApply = false;

        _suppressFontApply = true;
        SelectedFontFamily = _themeService.GetSelectedFontFamily() ?? "System Default";
        _suppressFontApply = false;

        _themeCustomization?.Refresh();
        _personalize?.Refresh();
    }

    partial void OnThemeFileMessageChanged(string value)
    {
        if (_personalize is not null)
        {
            _personalize.LookFileMessage = value;
        }
    }

    /// <summary>The variants of the active theme's family (Light / Dark ...), OLED left out; empty for a single-variant theme.</summary>
    public ObservableCollection<ThemeSummary> ActiveFamilyVariants { get; } = new();

    public bool HasActiveFamilyVariants => ActiveFamilyVariants.Count > 1;

    /// <summary>Re-syncs the gallery, the inspector's subject and the variant switch from <see cref="Themes"/>. Called by <c>RefreshThemes</c> and after a selection.</summary>
    private void RefreshThemeGallery()
    {
        ActiveThemeSummary = Themes.FirstOrDefault(t => t.IsActive);
        OnPropertyChanged(nameof(ActiveThemeSummary));

        if (_gallerySegmentChosen)
        {
            SyncVisibleThemes();
        }
        else
        {
            SetGallerySegment(ActiveThemeSummary?.Group ?? "Dark");
        }

        var variants = ActiveThemeSummary is { HasVariants: true } active
            ? Themes.Where(t => string.Equals(t.FamilyKey, active.FamilyKey, StringComparison.OrdinalIgnoreCase) && !t.IsGalleryHidden).ToList()
            : new List<ThemeSummary>();
        SyncCollection(ActiveFamilyVariants, variants);

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
