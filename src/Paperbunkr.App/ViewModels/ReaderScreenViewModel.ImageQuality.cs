using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Auto-levels, sharpen and auto-crop on the view-model side (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md #2 and #3). Auto-levels and auto-crop change the decoded pixels, so they
/// are handed to the pipeline (<see cref="IReaderPageProcessing"/>); sharpen is a paint-level filter the canvas applies itself. Kept in its own file so the already-large reader view model does not grow further.
/// </summary>
public partial class ReaderScreenViewModel
{
    /// <summary>Stretch each washed-out page's levels (CE's auto contrast). The default comes from Preferences (with the reader profile laid over it); a change here is stored on this issue.</summary>
    [ObservableProperty]
    private bool _autoLevels;

    /// <summary>Sharpening 0-3 (CE's range).</summary>
    [ObservableProperty]
    private int _sharpen;

    /// <summary>Bumped whenever the pipeline's processing changes, so a canvas that draws continuous pages asks the pipeline for them again.</summary>
    [ObservableProperty]
    private int _imageProcessingVersion;

    private bool _autoLevelsGlobalDefault;
    private int _sharpenGlobalDefault;
    private bool _autoCropSetting;
    private bool? _autoCropSession;
    private bool _suppressImageQualityPersist;
    private Dictionary<int, PageCropMode> _cropOverrides = new();

    /// <summary>Whether auto-crop is on for this visit: the setting, unless the palette switched it for this visit only.</summary>
    public bool AutoCrop => _autoCropSession ?? _autoCropSetting;

    /// <summary>The current page's own auto-crop choice (Automatic when it has none).</summary>
    public PageCropMode CurrentPageCropMode => _cropOverrides.TryGetValue(_currentPageIndex, out var mode) ? mode : PageCropMode.Auto;

    partial void OnAutoLevelsChanged(bool value)
    {
        if (_suppressImageQualityPersist)
        {
            return;
        }

        PersistImageQuality(issue => issue.AutoLevelsOverride = value == _autoLevelsGlobalDefault ? null : value);
        PushImageProcessing(refresh: true);
    }

    partial void OnSharpenChanged(int value)
    {
        if (_suppressImageQualityPersist)
        {
            return;
        }

        int clamped = Math.Clamp(value, 0, ImageAdjustmentMath.MaxSharpen);
        if (clamped != value)
        {
            Sharpen = clamped;
            return;
        }

        PersistImageQuality(issue => issue.SharpenOverride = value == _sharpenGlobalDefault ? null : value);
    }

    private void PersistImageQuality(Action<Issue> apply)
    {
        if (_suppressImageQualityPersist || _loadedIssueId is not int issueId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext(includeRemote: true);
        var issue = context.Issues.Find(issueId);
        if (issue is not null)
        {
            apply(issue);
            context.SaveChanges();
        }
    }

    /// <summary>
    /// Sets the three image quality values from the (profile-overlaid) settings and this issue's stored overrides, without writing anything. <paramref name="pushIfChanged"/> hands a change of auto-levels or
    /// auto-crop to the pipeline and redraws (a settings or profile change while the reader is open); a fresh load pushes once the pipeline exists instead.
    /// </summary>
    private void ApplyImageQualityDefaults(Issue? issue, AppSettings effective, bool pushIfChanged)
    {
        bool levelsBefore = AutoLevels;
        bool cropBefore = AutoCrop;
        _autoLevelsGlobalDefault = effective.DefaultAutoLevels;
        _sharpenGlobalDefault = Math.Clamp(effective.DefaultSharpen, 0, ImageAdjustmentMath.MaxSharpen);
        _autoCropSetting = effective.AutoCropMargins;

        _suppressImageQualityPersist = true;
        AutoLevels = issue?.AutoLevelsOverride ?? _autoLevelsGlobalDefault;
        Sharpen = Math.Clamp(issue?.SharpenOverride ?? _sharpenGlobalDefault, 0, ImageAdjustmentMath.MaxSharpen);
        _suppressImageQualityPersist = false;
        OnPropertyChanged(nameof(AutoCrop));

        if (pushIfChanged && (AutoLevels != levelsBefore || AutoCrop != cropBefore))
        {
            PushImageProcessing(refresh: true);
        }
    }

    /// <summary>Hands the pipeline what to do to pages from now on, and (when <paramref name="refresh"/>) draws the page again so the change shows.</summary>
    private void PushImageProcessing(bool refresh)
    {
        if (_decoder is not IReaderPageProcessing processing)
        {
            return;
        }

        processing.SetProcessing(new PageProcessingOptions(AutoLevels, AutoCrop), _cropOverrides);
        if (!refresh)
        {
            return;
        }

        ImageProcessingVersion++;
        if (!IsContinuousMode)
        {
            RefreshCurrentPage();
        }
    }

    private void LoadCropOverrides(PaperbunkrDbContext context, int issueId) =>
        _cropOverrides = context.PageCropOverrides.Where(o => o.IssueId == issueId).ToDictionary(o => o.PageNumber, o => o.Mode);

    /// <summary>Clears the visit-only auto-crop switch (called from <c>GoBack</c>, like the session profile).</summary>
    private void ClearImageQualitySession() => _autoCropSession = null;

    /// <summary>Resets this issue's auto-levels and sharpen to the defaults (part of the ADJUST section's Reset).</summary>
    private void ResetImageQuality(Issue? issue)
    {
        _suppressImageQualityPersist = true;
        bool levelsChanged = AutoLevels != _autoLevelsGlobalDefault;
        AutoLevels = _autoLevelsGlobalDefault;
        Sharpen = _sharpenGlobalDefault;
        _suppressImageQualityPersist = false;
        if (issue is not null)
        {
            issue.AutoLevelsOverride = null;
            issue.SharpenOverride = null;
        }

        if (levelsChanged)
        {
            PushImageProcessing(refresh: true);
        }
    }

    // ===================== Commands =====================

    [RelayCommand]
    private void ToggleAutoLevels() => AutoLevels = !AutoLevels;

    /// <summary>Palette: off, 1, 2, 3 and round again.</summary>
    [RelayCommand]
    private void CycleSharpen() => Sharpen = (Sharpen + 1) % (ImageAdjustmentMath.MaxSharpen + 1);

    /// <summary>Palette: switches auto-crop for this visit only (the setting is not touched).</summary>
    [RelayCommand]
    private void ToggleAutoCrop()
    {
        _autoCropSession = !AutoCrop;
        OnPropertyChanged(nameof(AutoCrop));
        PushImageProcessing(refresh: true);
        ToastRequested?.Invoke(new ToastRequest(AutoCrop ? "Auto-crop on" : "Auto-crop off", "For this visit only. The setting is in Preferences → Reader."));
    }

    [RelayCommand]
    private void SetCurrentPageCropAuto() => SetPageCropMode(_currentPageIndex, PageCropMode.Auto);

    [RelayCommand]
    private void SetCurrentPageCropNever() => SetPageCropMode(_currentPageIndex, PageCropMode.Never);

    [RelayCommand]
    private void SetCurrentPageCropAlways() => SetPageCropMode(_currentPageIndex, PageCropMode.Always);

    /// <summary>Stores a page's own auto-crop choice (Automatic deletes the row) and shows the page again.</summary>
    internal void SetPageCropMode(int pageIndex, PageCropMode mode)
    {
        if (_loadedIssueId is not int issueId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext(includeRemote: true))
        {
            var row = context.PageCropOverrides.FirstOrDefault(o => o.IssueId == issueId && o.PageNumber == pageIndex);
            if (mode == PageCropMode.Auto)
            {
                if (row is not null)
                {
                    context.PageCropOverrides.Remove(row);
                }
            }
            else if (row is null)
            {
                context.PageCropOverrides.Add(new PageCropOverride { IssueId = issueId, PageNumber = pageIndex, Mode = mode });
            }
            else
            {
                row.Mode = mode;
            }

            context.SaveChanges();
        }

        if (mode == PageCropMode.Auto)
        {
            _cropOverrides.Remove(pageIndex);
        }
        else
        {
            _cropOverrides[pageIndex] = mode;
        }

        OnPropertyChanged(nameof(CurrentPageCropMode));
        PushImageProcessing(refresh: true);
        ToastRequested?.Invoke(new ToastRequest(
            mode switch
            {
                PageCropMode.Never => "This page is never cropped",
                PageCropMode.Always => "This page is always cropped",
                _ => "This page follows the auto-crop setting",
            }));
    }

    /// <summary>Palette "Show crop": says what auto-crop finds on the current page (tuning aid; the detector's thresholds are constants in <see cref="PageCropDetector"/>).</summary>
    [RelayCommand]
    private void ShowCrop()
    {
        if (_decoder is not IReaderPageProcessing processing)
        {
            return;
        }

        var crop = processing.DetectCrop(_currentPageIndex);
        ToastRequested?.Invoke(crop is { } c
            ? new ToastRequest("Auto-crop would trim", $"Left {c.Left:P0}, top {c.Top:P0}, right {c.Right:P0}, bottom {c.Bottom:P0} of this page.")
            : new ToastRequest("Nothing to trim", "No plain scan border found on this page."));
    }
}
