using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Guided panel view and smart double-click zoom on the view-model side (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md sections 3 and 4): the on/off state, the detected panels of
/// the page on screen, and the settings. The canvas does the stepping. Kept in its own file so the already-large reader view model does not grow further.
/// </summary>
public partial class ReaderScreenViewModel
{
    private readonly PagePanelCache _panelCache = new();
    private PagePanelDetectionQueue? _panelDetectionQueue;
    private bool? _configuredGuidedView;

    private PagePanelDetectionQueue PanelDetectionQueue => _panelDetectionQueue ??= new(
        (page, rtl) => PanelAnalyzer(page, rtl),
        ms => Services.Reader.ReaderPerfStats.Current.RecordPanelDetectionMs(ms));

    /// <summary>Detection seam (default: <see cref="PagePanelAnalyzer.Analyze(Bitmap, bool)"/>); tests substitute a fast fake.</summary>
    internal Func<Bitmap, bool, PagePanels> PanelAnalyzer { get; set; } = PagePanelAnalyzer.Analyze;

    /// <summary>Guided panel view is on: next and previous step through the page's panels. Paged, single-page layout only.</summary>
    [ObservableProperty]
    private bool _isGuidedView;

    /// <summary>The detected panels of the page on screen (reading order), or null while unknown. Cleared the moment the page changes.</summary>
    [ObservableProperty]
    private PagePanels? _currentPagePanels;

    /// <summary>A double-click zooms to the panel under the pointer (<see cref="AppSettings.SmartDoubleClickZoom"/>).</summary>
    [ObservableProperty]
    private bool _smartDoubleClickZoom = true;

    /// <summary>Whether reading in reading order right-to-left, which decides the order of side-by-side panels.</summary>
    private bool PanelsRightToLeft => EffectiveReadingMode == ReadingMode.RightToLeft;

    [RelayCommand]
    private void ToggleGuidedView()
    {
        if (IsContinuousMode)
        {
            ToastRequested?.Invoke(new ToastRequest("Guided view is for paged reading"));
            return;
        }

        if (EffectivePageLayoutMode == PageLayoutMode.Double)
        {
            ToastRequested?.Invoke(new ToastRequest("Guided view needs single-page layout", "Turn double-page mode off first."));
            return;
        }

        IsGuidedView = !IsGuidedView;
        if (IsGuidedView)
        {
            // Nothing else says what guided view does, so the first thing after switching it on is how to use it.
            ToastRequested?.Invoke(new ToastRequest("Guided view on", "The next key, Space or a click steps through the panels of the page, then on to the next page. G turns it off."));
        }
    }

    partial void OnIsGuidedViewChanged(bool value)
    {
        if (value)
        {
            RequestPanels();
        }
    }

    /// <summary>
    /// Finds panels for the current page in the background. A page seen recently comes from the cache at once; duplicate requests share one serialized analysis.
    /// </summary>
    private void RequestPanels()
    {
        if (!IsGuidedView || CurrentPage is not { } page || _loadedIssueId is not int issueId)
        {
            return;
        }

        int pageIndex = _currentPageIndex;
        bool rtl = PanelsRightToLeft;
        PanelDetectionQueue.SetCurrent(new PagePanelDetectionKey(issueId, pageIndex, rtl));
        if (_panelCache.TryGet(issueId, pageIndex, rtl, out var cached))
        {
            CurrentPagePanels = cached;
            return;
        }

        _ = ResolveAndApplyPanelsAsync(page, issueId, pageIndex, rtl, guidedOnly: true);
    }

    /// <summary>Requests the current page's panels without blocking the canvas input event.</summary>
    [RelayCommand]
    private async Task EnsurePanelsAsync()
    {
        if (CurrentPagePanels is not null || CurrentPage is not { } page || _loadedIssueId is not int issueId)
        {
            return;
        }

        await ResolveAndApplyPanelsAsync(page, issueId, _currentPageIndex, PanelsRightToLeft, guidedOnly: false);
    }

    private async Task<PagePanels?> ResolvePanelsAsync(Bitmap page, int issueId, int pageIndex, bool rtl, bool keepWhenStale = false)
    {
        if (_panelCache.TryGet(issueId, pageIndex, rtl, out var cached))
        {
            return cached;
        }

        var key = new PagePanelDetectionKey(issueId, pageIndex, rtl);
        PanelDetectionQueue.SetCurrent(CurrentPage is not null && _loadedIssueId is int currentIssue
            ? new PagePanelDetectionKey(currentIssue, _currentPageIndex, PanelsRightToLeft)
            : null);
        var panels = await PanelDetectionQueue.RequestAsync(key, page, keepWhenStale);
        // A late interactive/guided result may have been produced from a bitmap the reader disposed
        // while moving quickly. Only cache while that exact page remains active.
        if (panels is not null && IsSamePanelPage(page, issueId, pageIndex, rtl))
        {
            _panelCache.Set(issueId, pageIndex, rtl, panels);
        }

        return panels;
    }

    private async Task ResolveAndApplyPanelsAsync(Bitmap page, int issueId, int pageIndex, bool rtl, bool guidedOnly)
    {
        var panels = await ResolvePanelsAsync(page, issueId, pageIndex, rtl);
        if (panels is not null && IsSamePanelPage(page, issueId, pageIndex, rtl) && (!guidedOnly || IsGuidedView))
        {
            CurrentPagePanels = panels;
        }
    }

    private bool IsSamePanelPage(Bitmap page, int issueId, int pageIndex, bool rtl) =>
        ReferenceEquals(CurrentPage, page) && _loadedIssueId == issueId && _currentPageIndex == pageIndex && PanelsRightToLeft == rtl;

    // ===================== "Show detected panels" tuning overlay (design section 5) =====================

    private static readonly TimeSpan PanelFlashLifetime = TimeSpan.FromSeconds(3);
    private DispatcherTimer? _panelFlashTimer;

    /// <summary>The numbered panel rectangles are showing over the page.</summary>
    [ObservableProperty]
    private bool _isPanelFlashVisible;

    /// <summary>Detects the panels of the page on screen (if not known) and shows them over the page for about three seconds, so a user can see what guided view and smart zoom would use.</summary>
    [RelayCommand]
    private async Task ShowDetectedPanelsAsync()
    {
        if (IsContinuousMode)
        {
            ToastRequested?.Invoke(new ToastRequest("Panels are detected in paged reading"));
            return;
        }

        if (CurrentPageSecondary is not null)
        {
            ToastRequested?.Invoke(new ToastRequest("Panel detection needs single-page layout"));
            return;
        }

        if (CurrentPage is not { } page || _loadedIssueId is not int issueId)
        {
            return;
        }

        int pageIndex = _currentPageIndex;
        bool rtl = PanelsRightToLeft;
        ToastRequested?.Invoke(new ToastRequest("Finding panels…"));
        var panels = await ResolvePanelsAsync(page, issueId, pageIndex, rtl);
        if (panels is null || !IsSamePanelPage(page, issueId, pageIndex, rtl))
        {
            return;
        }

        CurrentPagePanels = panels;
        IsPanelFlashVisible = true;
        if (_panelFlashTimer is null)
        {
            _panelFlashTimer = new DispatcherTimer { Interval = PanelFlashLifetime };
            _panelFlashTimer.Tick += OnPanelFlashExpired;
        }

        _panelFlashTimer.Stop();
        _panelFlashTimer.Start();
    }

    /// <summary>Where "Report bad panel detection" saves pages (a test seam; the default is a <c>panel-reports</c> folder next to the library database).</summary>
    internal Func<string> PanelReportFolder { get; set; } = () => Path.Combine(Path.GetDirectoryName(PaperbunkrDbContext.GetDefaultDatabasePath()) ?? Path.GetTempPath(), "panel-reports");

    /// <summary>
    /// Saves the page on screen and what panel detection made of it, so a page it got wrong can be kept as a test case (docs/superpowers/specs/2026-09-28-guided-view-detection-upgrade-design.md step 4).
    /// Two files in a new timestamped subfolder: <c>page.png</c> and <c>detection.json</c> (panel rectangles as fractions of the page, in reading order). Nothing leaves the computer.
    /// </summary>
    [RelayCommand]
    private async Task ReportBadPanelsAsync()
    {
        if (CurrentPage is not { } page || _loadedIssueId is not int issueId)
        {
            return;
        }

        int pageIndex = _currentPageIndex;
        bool rtl = PanelsRightToLeft;
        Bitmap reportPage;
        try
        {
            // Own the image before leaving the page; the reader may release its decoder bitmap while
            // inference and PNG writing are still running.
            reportPage = page.CreateScaledBitmap(page.PixelSize, BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            ToastRequested?.Invoke(new ToastRequest("Couldn't save the page", ex.Message));
            return;
        }

        ToastRequested?.Invoke(new ToastRequest("Preparing panel report…"));
        try
        {
            var panels = await ResolvePanelsAsync(reportPage, issueId, pageIndex, rtl, keepWhenStale: true) ?? PagePanels.Whole;
            string? folder = null;
            try
            {
                await Task.Run(() =>
                {
                    string destination = Path.Combine(PanelReportFolder(), $"{DateTime.Now:yyyyMMdd-HHmmss}-issue{issueId}-page{pageIndex + 1}");
                    folder = destination;
                    Directory.CreateDirectory(destination);
                    using (var file = File.Create(Path.Combine(destination, "page.png")))
                    {
                        reportPage.Save(file, PngBitmapEncoderOptions.Default);
                    }

                    var json = JsonSerializer.Serialize(new
                    {
                        issueId,
                        pageNumber = pageIndex + 1,
                        rightToLeft = rtl,
                        confident = panels.Confident,
                        modelAvailable = PanelDetectionService.OnnxAvailable,
                        panels = panels.Rects.Select(r => new { r.X, r.Y, r.Width, r.Height }),
                    }, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(destination, "detection.json"), json);
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException or ArgumentException)
            {
                if (folder is not null)
                {
                    try { Directory.Delete(folder, recursive: true); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                }

                ToastRequested?.Invoke(new ToastRequest("Couldn't save the page", ex.Message));
                return;
            }

            ToastRequested?.Invoke(new ToastRequest("Page saved for panel tuning", folder));
        }
        finally
        {
            reportPage.Dispose();
        }
    }

    /// <summary>Test seam, same rationale as <see cref="OnSkippedPagesHintExpired"/>.</summary>
    internal void OnPanelFlashExpired(object? sender, EventArgs e)
    {
        _panelFlashTimer?.Stop();
        IsPanelFlashVisible = false;
    }

    /// <summary>Applies the panel settings: the smart double-click toggle, and guided view's Preferences (or profile) default, which only takes effect when that default changes so a visit's own `G` is not undone.</summary>
    private void ApplyPanelSettings(AppSettings appSettings, AppSettings effective)
    {
        SmartDoubleClickZoom = appSettings.SmartDoubleClickZoom;
        if (_configuredGuidedView != effective.GuidedViewOnOpen)
        {
            _configuredGuidedView = effective.GuidedViewOnOpen;
            IsGuidedView = effective.GuidedViewOnOpen && !IsContinuousMode && EffectivePageLayoutMode != PageLayoutMode.Double;
        }
    }
}
