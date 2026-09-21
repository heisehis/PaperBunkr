using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Cached snapshot of the cosmetic-thumbnail <see cref="AppSettings"/> toggles (docs/superpowers/
/// specs/2026-09-13-preferences-cosmetic-toggles-design.md) for hot per-tile-realization reads
/// (<see cref="AsyncCoverImage"/>'s fade, the dog-ear peek, the hover tooltip) that can't afford a
/// <see cref="PaperbunkrDb.CreateContext"/> round-trip per call the way a per-navigation settings
/// read (e.g. <see cref="ThemeService.GetReducedMotion"/>) can. Refreshed once at startup
/// (<see cref="RefreshFrom"/>) and pushed again from <c>LibraryScreenViewModel</c> whenever the
/// toolbar toggle changes, so a live edit takes effect on the next tile realization without a
/// restart.
/// </summary>
public static class CosmeticThumbnailSettings
{
    public static bool FadeInThumbnails { get; set; } = true;
    public static bool DogEarThumbnails { get; set; } = true;
    public static bool ShowToolTips { get; set; }
    public static bool NumericRatingThumbnails { get; set; } = true;

    private static bool _bindingSpine = true;
    private static bool _progressRing = true;
    private static bool _showSelectionCheckbox;
    private static bool _heroBackdrop = true;
    private static bool _seriesAccentColor;

    /// <summary>Raised when a cosmetics-pitch overlay toggle changes (docs/superpowers/specs/2026-09-21-
    /// cosmetics-pitch-design.md), so already-realized tiles can repaint without a restart. Always UI-thread.</summary>
    public static event Action? OverlaySettingsChanged;

    /// <summary>Binding-spine texture on Poster/Panorama covers (cosmetics pitch #1). Default on.</summary>
    public static bool BindingSpine
    {
        get => _bindingSpine;
        set
        {
            if (_bindingSpine == value)
            {
                return;
            }

            _bindingSpine = value;
            OverlaySettingsChanged?.Invoke();
        }
    }

    /// <summary>Read-progress ring on Poster/Panorama covers (cosmetics pitch #2). Default on.</summary>
    public static bool ProgressRing
    {
        get => _progressRing;
        set
        {
            if (_progressRing == value)
            {
                return;
            }

            _progressRing = value;
            OverlaySettingsChanged?.Invoke();
        }
    }

    /// <summary>Hover multi-select checkbox on Library grid tiles. Default off (Ctrl/Shift+click selects); a selected tile still shows its checked box.</summary>
    public static bool ShowSelectionCheckbox
    {
        get => _showSelectionCheckbox;
        set
        {
            if (_showSelectionCheckbox == value)
            {
                return;
            }

            _showSelectionCheckbox = value;
            OverlaySettingsChanged?.Invoke();
        }
    }

    /// <summary>Blurred cover backdrop behind the Detail hero (cosmetics pitch 2 #8). Default on.</summary>
    public static bool HeroBackdrop
    {
        get => _heroBackdrop;
        set
        {
            if (_heroBackdrop == value)
            {
                return;
            }

            _heroBackdrop = value;
            OverlaySettingsChanged?.Invoke();
        }
    }

    /// <summary>Cover-derived accent on Detail screens (cosmetics pitch 2 #9). Default off.</summary>
    public static bool SeriesAccentColor
    {
        get => _seriesAccentColor;
        set
        {
            if (_seriesAccentColor == value)
            {
                return;
            }

            _seriesAccentColor = value;
            OverlaySettingsChanged?.Invoke();
        }
    }

    public static void RefreshFrom(AppSettings settings)
    {
        FadeInThumbnails = settings.FadeInThumbnails;
        DogEarThumbnails = settings.DogEarThumbnails;
        ShowToolTips = settings.ShowToolTips;
        NumericRatingThumbnails = settings.NumericRatingThumbnails;
        BindingSpine = settings.BindingSpine;
        ProgressRing = settings.ProgressRing;
        ShowSelectionCheckbox = settings.ShowSelectionCheckbox;
        HeroBackdrop = settings.HeroBackdrop;
        SeriesAccentColor = settings.SeriesAccentColor;
    }
}
