using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Preferences &gt; Reader: auto-levels, sharpen and auto-crop defaults (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md). Every change persists and raises
/// <see cref="ReaderDisplaySettingsChanged"/> so an open reader picks it up without a reload.
/// </summary>
public partial class PreferencesScreenViewModel
{
    /// <summary>Stretch each washed-out page's levels (CE's auto contrast).</summary>
    [ObservableProperty]
    private bool _defaultAutoLevels;

    /// <summary>Sharpening 0-3 (CE's range); 0 is off.</summary>
    [ObservableProperty]
    private int _defaultSharpen;

    /// <summary>Trim plain white or black scan borders so pages fill the window.</summary>
    [ObservableProperty]
    private bool _autoCropMargins;

    /// <summary>Show the summary in the reader's info panel straight away instead of behind "Show summary" (off: a summary can spoil).</summary>
    [ObservableProperty]
    private bool _infoPanelShowSummary;

    partial void OnInfoPanelShowSummaryChanged(bool value) => PersistComfort(s => s.InfoPanelShowSummary = value);

    partial void OnDefaultAutoLevelsChanged(bool value) => PersistComfort(s => s.DefaultAutoLevels = value);

    partial void OnDefaultSharpenChanged(int value) => PersistComfort(s => s.DefaultSharpen = Math.Clamp(value, 0, 3));

    partial void OnAutoCropMarginsChanged(bool value) => PersistComfort(s => s.AutoCropMargins = value);

    /// <summary>Loads the image quality settings; called from <c>Reload</c> inside its suppress-persist window.</summary>
    private void LoadImageQualitySettings(AppSettings settings)
    {
        DefaultAutoLevels = settings.DefaultAutoLevels;
        DefaultSharpen = settings.DefaultSharpen;
        AutoCropMargins = settings.AutoCropMargins;
        InfoPanelShowSummary = settings.InfoPanelShowSummary;
    }
}
