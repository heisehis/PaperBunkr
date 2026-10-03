using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Reader command palette (Ctrl+K), go-to-page (Ctrl+G) and the tap zone flash (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md sections 4-5).
/// Kept in its own file so the already-large reader view model does not grow further; the palette state itself lives in <see cref="ReaderCommandPaletteViewModel"/>.
/// </summary>
public partial class ReaderScreenViewModel
{
    private ReaderCommandPaletteViewModel? _palette;

    /// <summary>The Ctrl+K palette and Ctrl+G go-to-page prompt state, bound by the in-canvas overlay in <c>ReaderScreen.axaml</c>.</summary>
    public ReaderCommandPaletteViewModel Palette => _palette ??= new ReaderCommandPaletteViewModel(BuildPaletteEntries, () => PageCount, GoToPageFromPalette);

    /// <summary>True while the palette or the bad-page reason picker is showing: both take the keyboard (and the controller) for themselves, so the reader's own shortcuts stand down.</summary>
    public bool IsKeyboardOverlayOpen => Palette.IsOpen || IsReportPickerOpen;

    public string CommandPaletteHint => GetShortcutHint(InputActionIds.CommandPalette);

    [RelayCommand]
    private void ToggleCommandPalette() => Palette.Toggle();

    [RelayCommand]
    private void OpenGoToPage()
    {
        if (Palette.IsOpen && Palette.IsGoToPageMode)
        {
            Palette.Close();
            return;
        }

        Palette.OpenGoToPage();
    }

    /// <summary>A jump chosen in the palette: continuous mode scrolls to the page, paged mode goes through <see cref="JumpToPage"/> so a big jump shows the "Back to page N" chip.</summary>
    private void GoToPageFromPalette(int pageIndex)
    {
        if (PageCount <= 0)
        {
            return;
        }

        pageIndex = Math.Clamp(pageIndex, 0, PageCount - 1);
        if (IsContinuousMode)
        {
            ScrollToPageRequested?.Invoke(pageIndex);
        }
        else
        {
            JumpToPage(pageIndex);
        }
    }

    /// <summary>Jumps to the first page (Home); false when there is nothing loaded to jump within.</summary>
    public bool GoToFirstPage()
    {
        if (PageCount <= 0)
        {
            return false;
        }

        GoToPageFromPalette(0);
        return true;
    }

    /// <summary>Jumps to the last page (End); false when there is nothing loaded to jump within.</summary>
    public bool GoToLastPage()
    {
        if (PageCount <= 0)
        {
            return false;
        }

        GoToPageFromPalette(PageCount - 1);
        return true;
    }

    private static readonly (ReadingMode Mode, string Label)[] PaletteReadingModes =
    [
        (ReadingMode.LeftToRight, "Left to Right"),
        (ReadingMode.RightToLeft, "Right to Left"),
        (ReadingMode.TopToBottom, "Top to Bottom"),
        (ReadingMode.Webtoon, "Long Strip"),
        (ReadingMode.VerticalContinuous, "Longstrip (gapped)"),
        (ReadingMode.HorizontalContinuous, "Horizontal Long Strip"),
        (ReadingMode.HorizontalContinuousRightToLeft, "Horizontal Long Strip (RTL)"),
    ];

    private static readonly (ImageFitMode Mode, string Label)[] PaletteFitModes =
    [
        (ImageFitMode.Original, "Original size"),
        (ImageFitMode.Fit, "Fit page"),
        (ImageFitMode.FitWidth, "Fit width"),
        (ImageFitMode.FitHeight, "Fit height"),
        (ImageFitMode.BestFit, "Best fit"),
    ];

    /// <summary>Every action the palette offers, in the order shown before anything is typed. Shortcuts shown are the current (possibly remapped) ones.</summary>
    internal IReadOnlyList<ReaderPaletteEntry> BuildPaletteEntries()
    {
        var entries = new List<ReaderPaletteEntry>();

        void Add(string title, string group, string? actionId, Action run)
        {
            // The first keyboard binding reads best as a hint; an unbound action (or one with no action id) shows none.
            string? shortcut = actionId is null ? null : Input.ShortcutText(actionId);

            entries.Add(new ReaderPaletteEntry(title, group, shortcut, run));
        }

        // Navigate
        Add("Next page", "Navigate", InputActionIds.NextPage, () => NextPageCommand.Execute(null));
        Add("Previous page", "Navigate", InputActionIds.PreviousPage, () => PreviousPageCommand.Execute(null));
        Add("Go to page…", "Navigate", InputActionIds.GoToPage, () => Palette.OpenGoToPage());
        Add("Back to previous position", "Navigate", InputActionIds.JumpBack, () => JumpBackCommand.Execute(null));
        Add("Next chapter", "Navigate", null, () => NextChapterCommand.Execute(null));
        Add("Previous chapter", "Navigate", null, () => PreviousChapterCommand.Execute(null));
        Add("Next bookmark", "Navigate", InputActionIds.NextBookmark, () => NextBookmarkCommand.Execute(null));
        Add("Previous bookmark", "Navigate", InputActionIds.PreviousBookmark, () => PreviousBookmarkCommand.Execute(null));

        // Reading
        Add("Toggle reading direction (left-to-right / right-to-left)", "Reading", null, () => ToggleReadingModeCommand.Execute(null));
        foreach (var (mode, label) in PaletteReadingModes)
        {
            var captured = mode;
            Add($"Reading mode: {label}", "Reading", null, () => SetReadingModeCommand.Execute(captured));
        }

        Add("Guided view (panel by panel)", "Reading", InputActionIds.ToggleGuidedView, () => ToggleGuidedViewCommand.Execute(null));
        Add("Info panel (summary, credits, characters)", "Reading", InputActionIds.ToggleInfoPanel, () => ToggleInfoPanelCommand.Execute(null));
        Add("Note on this page", "Reading", null, () => NoteOnThisPageCommand.Execute(null));
        Add("Clip a region of this page", "Reading", InputActionIds.ClipRegion, () => ToggleClipModeCommand.Execute(null));
        Add("Export notes and clips…", "Reading", null, () => ExportNotesCommand.Execute(null));
        Add("Pin this page as a reference", "Reading", InputActionIds.PinPage, () => PinCurrentPageCommand.Execute(null));
        Add("Unpin the reference page", "Reading", null, () => UnpinPageCommand.Execute(null));
        Add("Pinned page: next size", "Reading", null, () => CyclePinSizeCommand.Execute(null));
        Add("Auto levels", "View", null, () => ToggleAutoLevelsCommand.Execute(null));
        Add("Sharpen: next level (off, 1, 2, 3)", "View", null, () => CycleSharpenCommand.Execute(null));
        Add("Auto-crop margins (this visit)", "View", null, () => ToggleAutoCropCommand.Execute(null));
        Add("Crop this page: follow the setting", "View", null, () => SetCurrentPageCropAutoCommand.Execute(null));
        Add("Crop this page: never", "View", null, () => SetCurrentPageCropNeverCommand.Execute(null));
        Add("Crop this page: always", "View", null, () => SetCurrentPageCropAlwaysCommand.Execute(null));
        Add("Show crop (what auto-crop finds)", "View", null, () => ShowCropCommand.Execute(null));
        Add("Toggle double-page spread", "Reading", null, () => ToggleDoublePageModeCommand.Execute(null));
        foreach (var (mode, label) in PaletteFitModes)
        {
            var captured = mode;
            string? id = mode switch
            {
                ImageFitMode.Original => InputActionIds.FitOriginal,
                ImageFitMode.Fit => InputActionIds.FitAll,
                ImageFitMode.FitWidth => InputActionIds.FitWidth,
                ImageFitMode.FitHeight => InputActionIds.FitHeight,
                _ => InputActionIds.FitBest,
            };
            Add($"Fit: {label}", "Reading", id, () => SetFitModeCommand.Execute(captured));
        }

        Add("Toggle auto-rotate landscape pages", "Reading", null, () => ToggleAutoRotateCommand.Execute(null));
        foreach (var style in Enum.GetValues<PageTransitionStyle>())
        {
            var captured = style;
            Add($"Page transition: {style}", "Reading", null, () => SetPageTransitionStyleCommand.Execute(captured));
        }

        // Profiles (design 2026-09-25 F2 section 3)
        Add("Profile: Standard", "Profile", InputActionIds.NextProfile, () => ApplySessionProfile(ReaderProfileSelector.StandardSessionId, "Standard"));
        foreach (var profile in _profileService.List(WorkspaceScreen.Reader))
        {
            var captured = profile;
            Add($"Profile: {profile.Name}", "Profile", null, () => ApplySessionProfile(captured.Id, captured.Name));
        }

        Add("Save current look as a profile…", "Profile", null, () => SaveCurrentAsProfileCommand.Execute(null));
        Add("Use the current profile for this series", "Profile", null, () => UseProfileForSeriesCommand.Execute(null));
        Add("Use the current profile as my default", "Profile", null, () => UseProfileAsDefaultCommand.Execute(null));

        // View
        Add("Toggle fullscreen", "View", InputActionIds.ToggleFullscreen, () => ToggleFullscreenCommand.Execute(null));
        Add("Toggle toolbar", "View", null, () => ToggleChromeCommand.Execute(null));
        Add("Toggle thumbnail rail", "View", null, () => ToggleRailCommand.Execute(null));
        Add("Show tap zones", "View", null, ShowTapZoneFlash);
        Add("Show detected panels", "View", null, () => ShowDetectedPanelsCommand.Execute(null));
        Add("Report bad panel detection (save this page)", "View", null, () => ReportBadPanelsCommand.Execute(null));
        Add("Zoom in", "View", InputActionIds.ZoomIn, () => ZoomInCommand.Execute(null));
        Add("Zoom out", "View", InputActionIds.ZoomOut, () => ZoomOutCommand.Execute(null));
        Add("Zoom: reset to fit (100%)", "View", null, () => ResetZoomCommand.Execute(null));
        Add("Rotate clockwise", "View", InputActionIds.RotateClockwise, () => RotateClockwiseCommand.Execute(null));
        Add("Rotate counter-clockwise", "View", InputActionIds.RotateCounterClockwise, () => RotateCounterClockwiseCommand.Execute(null));
        if (IsContinuousMode)
        {
            Add("Toggle auto-scroll", "View", InputActionIds.ToggleAutoScroll, () => ToggleAutoScrollCommand.Execute(null));
        }

        // Page
        Add("Rate this issue…", "Page", null, () => EndCardRateCommand.Execute(null));
        Add("Report a bad page…", "Page", InputActionIds.ReportBadPage, () => ReportBadPageCommand.Execute(null));
        Add("Toggle bookmark on this page", "Page", null, () => ToggleBookmarkCommand.Execute(null));
        Add("Copy page", "Page", InputActionIds.CopyPage, () => CopyPageCommand.Execute(null));
        if (IsSpreadShowing)
        {
            Add("Copy spread", "Page", null, () => CopySpreadCommand.Execute(null));
            Add("Save spread as PNG…", "Page", null, () => SaveSpreadAsPngCommand.Execute(null));
            Add("Save spread as JPEG…", "Page", null, () => SaveSpreadAsJpegCommand.Execute(null));
        }

        Add("Save page as PNG…", "Page", null, () => SavePageAsPngCommand.Execute(null));
        Add("Save page as JPEG…", "Page", null, () => SavePageAsJpegCommand.Execute(null));
        Add("Toggle reading stats", "View", InputActionIds.ToggleSessionHud, () => ToggleSessionHudCommand.Execute(null));
        Add("Toggle warm tint", "View", InputActionIds.ToggleWarmShift, () => ToggleWarmShiftCommand.Execute(null));

        // Leave
        Add("Back to the library", "Leave", null, () => GoBackCommand.Execute(null));

        return entries;
    }

    // ===================== Tap zone flash (design 2026-09-25 F1 section 4): the zone overlay shown briefly over the page =====================

    private static readonly TimeSpan TapZoneFlashLifetime = TimeSpan.FromSeconds(3);
    private DispatcherTimer? _tapZoneFlashTimer;

    [ObservableProperty]
    private bool _isTapZoneFlashVisible;

    /// <summary>The layout the flash draws: the paged or continuous one, whichever mode is showing.</summary>
    public TapZoneLayout TapZoneFlashLayout => IsContinuousMode ? ContinuousTapZoneLayout : PagedTapZoneLayout;

    public TapZoneInvert TapZoneFlashInvert => IsContinuousMode ? ContinuousTapZoneInvert : PagedTapZoneInvert;

    public bool TapZoneFlashIsRightToLeft => EffectiveReadingMode is ReadingMode.RightToLeft or ReadingMode.HorizontalContinuousRightToLeft;

    /// <summary>Shows the current tap zone layout over the page for about three seconds (palette "Show tap zones", and a profile switch that changes the layout).</summary>
    public void ShowTapZoneFlash()
    {
        OnPropertyChanged(nameof(TapZoneFlashLayout));
        OnPropertyChanged(nameof(TapZoneFlashInvert));
        OnPropertyChanged(nameof(TapZoneFlashIsRightToLeft));
        IsTapZoneFlashVisible = true;

        if (_tapZoneFlashTimer is null)
        {
            _tapZoneFlashTimer = new DispatcherTimer { Interval = TapZoneFlashLifetime };
            _tapZoneFlashTimer.Tick += OnTapZoneFlashExpired;
        }

        _tapZoneFlashTimer.Stop();
        _tapZoneFlashTimer.Start();
    }

    /// <summary>Test seam, same rationale as <see cref="OnSkippedPagesHintExpired"/>.</summary>
    internal void OnTapZoneFlashExpired(object? sender, EventArgs e)
    {
        _tapZoneFlashTimer?.Stop();
        IsTapZoneFlashVisible = false;
    }
}
