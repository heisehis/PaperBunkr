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
    private int _panelRequestId;
    private bool? _configuredGuidedView;

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

    [ObservableProperty]
    private IReadOnlyList<KeyGesture> _toggleGuidedViewKey = [new(Key.G)];

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
    /// Finds the panels of the page on screen in the background (guided view only; the smart double-click asks for them itself). A page seen recently comes from the cache at once. A result that arrives after the page
    /// changed again is dropped.
    /// </summary>
    private void RequestPanels()
    {
        if (!IsGuidedView || CurrentPage is not { } page || _loadedIssueId is not int issueId)
        {
            return;
        }

        int pageIndex = _currentPageIndex;
        bool rtl = PanelsRightToLeft;
        if (_panelCache.TryGet(issueId, pageIndex, rtl, out var cached))
        {
            CurrentPagePanels = cached;
            return;
        }

        int requestId = ++_panelRequestId;
        var analyzer = PanelAnalyzer;
        Task.Run(() => analyzer(page, rtl)).ContinueWith(task =>
        {
            var panels = task.IsCompletedSuccessfully ? task.Result : PagePanels.Whole;
            Dispatcher.UIThread.Post(() =>
            {
                _panelCache.Set(issueId, pageIndex, rtl, panels);
                if (requestId == _panelRequestId && _currentPageIndex == pageIndex && IsGuidedView)
                {
                    CurrentPagePanels = panels;
                }
            });
        });
    }

    /// <summary>
    /// Makes <see cref="CurrentPagePanels"/> available now, detecting on the calling thread if it is not cached: the smart double-click when guided view is off. With the ONNX model that is a few
    /// hundred milliseconds on a cold page (about 150 ms for a webtoon strip), a visible hitch; moving it off the UI thread is part of the deferred performance pass.
    /// </summary>
    [RelayCommand]
    private void EnsurePanels()
    {
        if (CurrentPagePanels is not null || CurrentPage is not { } page || _loadedIssueId is not int issueId)
        {
            return;
        }

        bool rtl = PanelsRightToLeft;
        if (!_panelCache.TryGet(issueId, _currentPageIndex, rtl, out var panels))
        {
            panels = PanelAnalyzer(page, rtl);
            _panelCache.Set(issueId, _currentPageIndex, rtl, panels);
        }

        CurrentPagePanels = panels;
    }

    // ===================== "Show detected panels" tuning overlay (design section 5) =====================

    private static readonly TimeSpan PanelFlashLifetime = TimeSpan.FromSeconds(3);
    private DispatcherTimer? _panelFlashTimer;

    /// <summary>The numbered panel rectangles are showing over the page.</summary>
    [ObservableProperty]
    private bool _isPanelFlashVisible;

    /// <summary>Detects the panels of the page on screen (if not known) and shows them over the page for about three seconds, so a user can see what guided view and smart zoom would use.</summary>
    [RelayCommand]
    private void ShowDetectedPanels()
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

        EnsurePanels();
        if (CurrentPagePanels is null)
        {
            return;
        }

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
    private void ReportBadPanels()
    {
        if (CurrentPage is not { } page || _loadedIssueId is not int issueId)
        {
            return;
        }

        EnsurePanels();
        var panels = CurrentPagePanels ?? PagePanels.Whole;
        try
        {
            string folder = Path.Combine(PanelReportFolder(), $"{DateTime.Now:yyyyMMdd-HHmmss}-issue{issueId}-page{_currentPageIndex + 1}");
            Directory.CreateDirectory(folder);
            using (var file = File.Create(Path.Combine(folder, "page.png")))
            {
                page.Save(file);
            }

            var json = JsonSerializer.Serialize(new
            {
                issueId,
                pageNumber = _currentPageIndex + 1,
                rightToLeft = PanelsRightToLeft,
                confident = panels.Confident,
                modelAvailable = PanelDetectionService.OnnxAvailable,
                panels = panels.Rects.Select(r => new { r.X, r.Y, r.Width, r.Height }),
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(folder, "detection.json"), json);
            ToastRequested?.Invoke(new ToastRequest("Page saved for panel tuning", folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            ToastRequested?.Invoke(new ToastRequest("Couldn't save the page", ex.Message));
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
