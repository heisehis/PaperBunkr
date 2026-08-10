using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SkiaSharp;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Reader screen, ported from ReaderScreen.dc.html (Claude Design project 43c40b25). Loads a real
/// <see cref="Issue"/> (via <see cref="LoadIssue"/>/<see cref="EnsureIssueLoaded"/>) and, as of
/// the Alpha-scope reader canvas (docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md),
/// actually decodes and renders its pages via <see cref="PageImageDecoder"/> for CBZ/CBR files —
/// the placeholder colored tile with a static page number is gone; <see cref="CurrentPage"/> is a
/// real decoded bitmap now. Continuous/webtoon rendering (onboarding.md §8) stays deferred to Beta.
/// </summary>
public partial class ReaderScreenViewModel : ViewModelBase
{
    // A single issue's page thumbnails aren't decoded eagerly (spec §5's virtualization principle
    // is specifically about not decoding pages that aren't needed) - still lightweight color-swatch
    // placeholders, just with correct count/selection tracking the real current page now.
    private const int MaxThumbnails = 200;

    // No injected context-factory seam needed here (unlike SkinService/CoverThumbnailService) -
    // KeyBindingService's own default ctor already goes through PaperbunkrDb.CreateContext(),
    // which PaperbunkrDbContext.DatabasePathOverride already redirects in tests.
    private readonly KeyBindingService _keyBindings = new();

    private readonly Action _goBack;
    private int? _loadedIssueId;
    private int? _loadedSeriesId;
    private IPageImageDecoder? _decoder;
    private int _currentPageIndex;
    private int _loadGeneration;
    private bool _isRightToLeft;

    /// <summary>
    /// Periodic backstop against the documented Avalonia native-bitmap-memory growth risk (issue
    /// #18498, docs/onboarding.md §8, docs/superpowers/specs/2026-08-10-reader-polish-continuous-
    /// scroll-chrome-overlays-design.md §3) - GPU resource cache tuning (Program.cs) and immediate
    /// disposal on eviction (PageDecodeService) are the primary mitigations, this is a backstop on
    /// top of both. Started once, on the first <see cref="Load"/>, and left running for the app's
    /// lifetime rather than "stopped on close": unlike a typical screen, this app's rail-nav screen
    /// switcher never destroys the Reader screen/VM (docs/superpowers/specs/2026-08-06-reader-
    /// canvas-alpha-design.md's ReaderScreen.axaml.cs comment) - it only toggles visibility - so
    /// there's no real "close" event to stop on, and purging an idle Reader's already-small cache
    /// periodically is harmless.
    /// </summary>
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromSeconds(30);
    private DispatcherTimer? _purgeTimer;

    public ReaderScreenViewModel(Action goBack)
    {
        _goBack = goBack;
        CoverBrush = SeriesCardSample.Gradient("#442a1c", "#c9803f");
        Thumbnails = new ObservableCollection<ReaderThumbnailSample>();
    }

    public ObservableCollection<ReaderThumbnailSample> Thumbnails { get; }

    public IBrush CoverBrush { get; private set; }
    public string BreadcrumbSeries { get; private set; } = string.Empty;
    public string IssueTitle { get; private set; } = string.Empty;
    public string ReadingModeLabel { get; private set; } = "Left to Right";
    public string PageLabel { get; private set; } = string.Empty;
    public double ProgressFraction { get; private set; }

    /// <summary>The raw effective mode (<c>Issue.ReadingModeOverride ?? Series.ReadingMode</c>), so <see cref="Views.PageCanvas"/> can pick its axis for continuous mode without re-deriving it from <see cref="ReadingModeLabel"/>'s display string.</summary>
    [ObservableProperty]
    private ReadingMode _effectiveReadingMode;

    [ObservableProperty]
    private Bitmap? _currentPage;

    [ObservableProperty]
    private bool _highQualityPageDisplay = true;

    [ObservableProperty]
    private Key _pageTurnLeftKey = Key.Left;

    [ObservableProperty]
    private Key _pageTurnRightKey = Key.Right;

    [ObservableProperty]
    private string? _errorMessage;

    private double _zoomLevel = 1.0;

    /// <summary>
    /// Hand-written rather than <c>[ObservableProperty]</c> (like <see cref="CoverBrush"/>/
    /// <see cref="BreadcrumbSeries"/> already are in this file) because it needs custom
    /// clamp-then-cascade logic the source generator can't express: this setter is the single
    /// mechanism satisfying "resets to fit" everywhere - <see cref="Load"/> and
    /// <see cref="Views.PageCanvas"/>'s double-click-reset path both just set
    /// <c>ZoomLevel = 1.0</c>, and the cascade zeroes pan for both, so neither caller separately
    /// zeroes pan. Range constants are duplicated in <see cref="Views.ZoomPanMath"/> rather than
    /// referenced from here, to avoid a ViewModels -&gt; Views dependency this codebase's binding
    /// direction doesn't otherwise have.
    /// </summary>
    /// <summary>
    /// <paramref name="clamped"/>'s upper bound is mode-aware (docs/superpowers/specs/2026-08-10-
    /// reader-polish-continuous-scroll-chrome-overlays-design.md §5): paged mode keeps the original
    /// fixed 4.0 ceiling, continuous mode is unclamped upward ("zoom is free and unclamped upward
    /// from that base... layered on top") - one setter, mode-aware, rather than a parallel property.
    /// </summary>
    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            // Continuous/webtoon mode: bounded 0.5x-4x (user direction, matching the toolbar zoom
            // slider - supersedes the originally-scoped "unclamped upward"). Paged mode: unchanged 1x-4x.
            double minZoom = IsContinuousMode ? 0.5 : 1.0;
            double maxZoom = IsContinuousMode ? 4.0 : Views.ZoomPanMath.MaxZoom;
            double clamped = Math.Clamp(value, minZoom, maxZoom);
            if (SetProperty(ref _zoomLevel, clamped) && clamped == 1.0)
            {
                PanOffsetX = 0;
                PanOffsetY = 0;
            }
        }
    }

    [ObservableProperty]
    private double _panOffsetX;

    [ObservableProperty]
    private double _panOffsetY;

    /// <summary>
    /// Continuous mode's scroll position, in stack space (docs/superpowers/specs/2026-08-10-reader-
    /// polish-continuous-scroll-chrome-overlays-design.md §2/§5/§6) - the main-axis analog of
    /// <see cref="PanOffsetX"/>/<see cref="PanOffsetY"/>, fed straight into
    /// <see cref="Views.ReaderLayoutModel.ComputeContinuousLayout"/>. Session-only, resets to 0 on
    /// every <see cref="Load"/>, same lifecycle as <see cref="ZoomLevel"/>.
    /// </summary>
    [ObservableProperty]
    private double _scrollOffset;

    /// <summary>
    /// Computed off the currently-effective <see cref="Entities.ReadingMode"/> (docs/superpowers/
    /// specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §5) - gates the
    /// fit-mode picker's visibility (continuous mode has no fit-mode concept, base scale always
    /// fills the cross axis) and <see cref="ZoomLevel"/>'s clamp ceiling above.
    /// </summary>
    [ObservableProperty]
    private bool _isContinuousMode;

    /// <summary>
    /// Exposed so <see cref="Views.PageCanvas"/> can pull whichever pages its continuous-mode layout
    /// window needs (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-
    /// overlays-design.md §4) - paged mode still only ever binds <see cref="CurrentPage"/> directly
    /// and never touches this. Manually raised (not <c>[ObservableProperty]</c>) since it's
    /// reassigned as a side effect of <see cref="Load"/> reopening <see cref="_decoder"/>, not
    /// something set directly by a command.
    /// </summary>
    public IPageImageDecoder? Decoder => _decoder;

    [ObservableProperty]
    private int _pageCount;

    private const double ZoomStep = 0.25;

    [ObservableProperty]
    private ImageFitMode _fitMode = ImageFitMode.FitWidth;

    [ObservableProperty]
    private bool _autoRotate;

    /// <summary>Session-only (spec §3) - resets on every <see cref="Load"/>, never persisted. Composed with <see cref="AutoRotate"/> inside <see cref="Views.PageCanvas"/>, which is the layer that actually knows the current page bitmap's landscape/portrait shape.</summary>
    [ObservableProperty]
    private int _manualRotationDegrees;

    /// <summary>Whether <see cref="ZoomLevel"/> resets to 1.0 on every page turn (docs/superpowers/specs/2026-08-10-preferences-reader-tab-design.md), read from <c>AppSettings.ResetZoomOnPageChange</c> on <see cref="Load"/>.</summary>
    private bool _resetZoomOnPageChange;

    [ObservableProperty]
    private double _mouseWheelSpeed = 2.0;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    /// <summary>Loads a specific issue by id (e.g. from Detail's Continue button).</summary>
    public void LoadIssue(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Include(i => i.Series).FirstOrDefault(i => i.Id == issueId);
        if (issue?.Series is null)
        {
            return;
        }

        Load(issue, issue.Series, context);
    }

    /// <summary>
    /// Loads the currently-open issue's data unchanged, or - if the Reader has never been opened
    /// this session (e.g. the rail nav button clicked with nothing else selected yet) - falls
    /// back to the first issue of the first series in the library, so the screen never shows a
    /// blank/broken state.
    /// </summary>
    public void EnsureIssueLoaded()
    {
        if (_loadedIssueId is not null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Include(s => s.Issues).OrderBy(s => s.SortName ?? s.Name).FirstOrDefault();
        var issue = series?.Issues.OrderByNumber().FirstOrDefault();
        if (series is not null && issue is not null)
        {
            Load(issue, series, context);
        }
    }

    /// <summary>
    /// Loads an issue. <paramref name="forcedStartPage"/> is set only by
    /// <see cref="NavigateToAdjacentIssue"/> - crossing an issue boundary always lands on a
    /// specific page (0 going forward, the last page going backward) regardless of the
    /// <c>OpenLastPage</c> preference, which only governs *reopening* an issue from elsewhere
    /// (Detail's Continue button, the rail nav). Passing <see cref="int.MaxValue"/> is a
    /// deliberate "clamp to the last page" sentinel - the real page count isn't known until after
    /// the decoder opens below.
    /// </summary>
    private void Load(Issue issue, Series series, PaperbunkrDbContext context, int? forcedStartPage = null)
    {
        int generation = ++_loadGeneration;

        if (_purgeTimer is null)
        {
            _purgeTimer = new DispatcherTimer { Interval = PurgeInterval };
            _purgeTimer.Tick += (_, _) => SKGraphics.PurgeResourceCache();
            _purgeTimer.Start();
        }

        _decoder?.Dispose();
        _decoder = null;
        _loadedIssueId = issue.Id;
        _loadedSeriesId = series.Id;

        CoverBrush = SeriesCardSample.CoverBrushFor(series.Name);
        BreadcrumbSeries = $"Library / {series.Name} /";
        IssueTitle = string.IsNullOrWhiteSpace(issue.Title)
            ? $"Issue #{issue.Number}"
            : $"Issue #{issue.Number} — {issue.Title}";

        var appSettings = context.GetOrCreateAppSettings();
        HighQualityPageDisplay = appSettings.HighQualityPageDisplay;
        MouseWheelSpeed = appSettings.MouseWheelSpeed;
        _resetZoomOnPageChange = appSettings.ResetZoomOnPageChange;
        PageTurnLeftKey = _keyBindings.GetKey(context, KeyboardCommandRegistry.ReaderPageTurnLeft);
        PageTurnRightKey = _keyBindings.GetKey(context, KeyboardCommandRegistry.ReaderPageTurnRight);
        UpdateReadingModeState(issue.ReadingModeOverride ?? series.ReadingMode, appSettings.ReverseRtlNavigation);

        ErrorMessage = null;
        ZoomLevel = 1.0;
        ScrollOffset = 0;
        ManualRotationDegrees = 0;
        FitMode = issue.PageFitModeOverride ?? appSettings.DefaultPageFitMode;
        AutoRotate = issue.AutoRotateOverride ?? appSettings.DefaultAutoRotate;
        int pageCount = issue.PageCount is > 0 ? issue.PageCount.Value : 1;

        if (!string.IsNullOrEmpty(issue.FilePath))
        {
            // Continuous mode needs the two-tier/virtualized decoder (multiple pages concurrently
            // visible); paged mode keeps its original ±1-window decoder, untouched (docs/
            // superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md
            // §1's Stage 1 note - both implement IPageImageDecoder, so nothing downstream of this
            // needs to know which one is active except the continuous-specific calls PageCanvas
            // makes directly against PageDecodeService).
            _decoder = IsContinuousMode ? PageDecodeService.TryOpen(issue.FilePath) : PageImageDecoder.TryOpen(issue.FilePath);
            if (_decoder is null)
            {
                ErrorMessage = "Couldn't open this file — unsupported format or a damaged archive.";
            }
            else
            {
                pageCount = _decoder.PageCount > 0 ? _decoder.PageCount : pageCount;
                // Self-healing metadata (spec §3): the real archive is the source of truth once
                // we've actually opened it, not whatever value was stored (seeded/migrated/guessed).
                if (issue.PageCount != pageCount)
                {
                    issue.PageCount = pageCount;
                    context.SaveChanges();
                }
            }
        }
        else
        {
            ErrorMessage = "This issue has no file linked yet.";
        }

        PageCount = pageCount;
        OnPropertyChanged(nameof(Decoder));

        if (forcedStartPage is int forced)
        {
            _currentPageIndex = Math.Clamp(forced, 0, pageCount - 1);
        }
        else
        {
            bool openLastPage = appSettings.OpenLastPage;
            _currentPageIndex = Math.Clamp(openLastPage ? issue.LastPageRead ?? 0 : 0, 0, pageCount - 1);
        }

        PageLabel = $"PAGE {_currentPageIndex + 1} / {pageCount}";
        ProgressFraction = pageCount > 1 ? (double)_currentPageIndex / (pageCount - 1) : 0;

        Thumbnails.Clear();
        int thumbnailCount = Math.Min(pageCount, MaxThumbnails);
        for (int page = 0; page < thumbnailCount; page++)
        {
            Thumbnails.Add(new ReaderThumbnailSample { CoverBrush = CoverBrush, IsSelected = page == _currentPageIndex });
        }

        OnPropertyChanged(nameof(CoverBrush));
        OnPropertyChanged(nameof(BreadcrumbSeries));
        OnPropertyChanged(nameof(IssueTitle));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(ProgressFraction));

        RefreshCurrentPage();
        StartThumbnailGeneration(generation, thumbnailCount);
    }

    /// <summary>Shared by <see cref="Load"/>, <see cref="ToggleReadingMode"/>, and <see cref="SetReadingMode"/> so the label/spatial-flip/continuous-mode switches can't drift apart between them.</summary>
    private void UpdateReadingModeState(ReadingMode effectiveMode, bool reverseRtlNavigation)
    {
        _isRightToLeft = effectiveMode == ReadingMode.RightToLeft && reverseRtlNavigation;
        IsContinuousMode = effectiveMode is ReadingMode.VerticalContinuous or ReadingMode.HorizontalContinuous
            or ReadingMode.HorizontalContinuousRightToLeft or ReadingMode.Webtoon;
        EffectiveReadingMode = effectiveMode;
        ReadingModeLabel = effectiveMode switch
        {
            ReadingMode.RightToLeft => "Right to Left ▾",
            ReadingMode.VerticalContinuous => "Vertical (Continuous) ▾",
            ReadingMode.HorizontalContinuous => "Horizontal (Continuous) ▾",
            ReadingMode.HorizontalContinuousRightToLeft => "Horizontal RTL (Continuous) ▾",
            ReadingMode.Webtoon => "Webtoon ▾",
            _ => "Left to Right ▾",
        };
        OnPropertyChanged(nameof(ReadingModeLabel));
    }

    /// <summary>
    /// P6 fix (docs/alpha-todo.md) - this pill was previously non-interactive, styled identically to
    /// the working toggle in <see cref="DetailTabsViewModel"/> (which this mirrors: a binary
    /// LTR/RTL flip, not a full mode picker - <see cref="ReadingMode.VerticalContinuous"/>/
    /// <see cref="ReadingMode.HorizontalContinuous"/> collapse to <see cref="ReadingMode.RightToLeft"/>
    /// same as there, per docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md §5).
    /// Writes <c>Series.ReadingMode</c>, not <c>Issue.ReadingModeOverride</c> - nothing in this app
    /// writes that field yet, it stays dormant.
    /// </summary>
    [RelayCommand]
    private void ToggleReadingMode()
    {
        if (_loadedSeriesId is not int seriesId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return;
        }

        series.ReadingMode = series.ReadingMode == ReadingMode.RightToLeft ? ReadingMode.LeftToRight : ReadingMode.RightToLeft;
        context.SaveChanges();

        var issue = _loadedIssueId is int issueId ? context.Issues.Find(issueId) : null;
        UpdateReadingModeState(issue?.ReadingModeOverride ?? series.ReadingMode, context.GetOrCreateAppSettings().ReverseRtlNavigation);
    }

    /// <summary>
    /// The full 4-option picker <see cref="ToggleReadingMode"/> deliberately isn't (its own doc
    /// comment: "a binary LTR/RTL flip, not a full mode picker") - needed so continuous mode
    /// (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md
    /// §4/§5) is actually reachable at all, since nothing else in this app writes
    /// <see cref="ReadingMode.VerticalContinuous"/>/<see cref="ReadingMode.HorizontalContinuous"/>.
    /// Same <c>Series.ReadingMode</c> write target as <see cref="ToggleReadingMode"/>, not
    /// <c>Issue.ReadingModeOverride</c>.
    ///
    /// Unlike <see cref="ToggleReadingMode"/> (which only ever flips between LeftToRight/
    /// RightToLeft, never touching decoder choice), this can cross the paged/continuous boundary -
    /// real bug found via manual testing: paged mode's <c>PageImageDecoder</c> and continuous
    /// mode's <c>PageDecodeService</c> aren't interchangeable (the former's ±1-window eviction
    /// disposes bitmaps out from under a continuous-mode layout pass that requests several pages in
    /// one batch, producing a blank/corrupted screen), so switching modes has to reopen the decoder
    /// via a full <see cref="LoadIssue"/>, not just flip the state flags in place. Resets zoom/pan/
    /// scroll/current-page as a result - reasonable here since "position" itself means something
    /// different across the paged/continuous boundary (a page index vs. a scroll offset), unlike
    /// <see cref="ToggleReadingMode"/>'s LTR/RTL flip where preserving position makes sense.
    /// </summary>
    [RelayCommand]
    private void SetReadingMode(ReadingMode mode)
    {
        if (_loadedSeriesId is not int seriesId || _loadedIssueId is not int issueId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = context.Series.FirstOrDefault(s => s.Id == seriesId);
            if (series is null)
            {
                return;
            }

            series.ReadingMode = mode;
            context.SaveChanges();
        }

        LoadIssue(issueId);
    }

    /// <summary>
    /// Unlike <see cref="ToggleReadingMode"/> (writes <c>Series.ReadingMode</c>), this writes the
    /// per-book <c>Issue.PageFitModeOverride</c> directly - page dimensions/scan quality genuinely
    /// vary issue-to-issue in a way reading direction doesn't (docs/superpowers/specs/
    /// 2026-08-10-reader-polish-core-viewing-controls-design.md §3).
    /// </summary>
    [RelayCommand]
    private void SetFitMode(ImageFitMode mode)
    {
        FitMode = mode;
        if (_loadedIssueId is not int issueId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(issueId);
        if (issue is not null)
        {
            issue.PageFitModeOverride = mode;
            context.SaveChanges();
        }
    }

    /// <summary>Same per-book override shape as <see cref="SetFitMode"/>, for the auto-rotate-landscape-pages toggle.</summary>
    [RelayCommand]
    private void ToggleAutoRotate()
    {
        AutoRotate = !AutoRotate;
        if (_loadedIssueId is not int issueId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(issueId);
        if (issue is not null)
        {
            issue.AutoRotateOverride = AutoRotate;
            context.SaveChanges();
        }
    }

    /// <summary>Session-only, never persisted (spec §3) - one press = +90 degrees, wraps at 360.</summary>
    [RelayCommand]
    private void RotateClockwise() => ManualRotationDegrees = (ManualRotationDegrees + 90) % 360;

    // Discrete no-arg commands rather than a single SetZoomPreset(double) - a raw string
    // CommandParameter (e.g. "1.5") bound in XAML has no compile-time check that it'll actually
    // parse/cast to double at Execute time, unlike the x:Static-enum-CommandParameter pattern used
    // for fit mode above (a real typed value, not a string Avalonia has to convert).
    [RelayCommand]
    private void SetZoom100() => ZoomLevel = 1.0;

    [RelayCommand]
    private void SetZoom125() => ZoomLevel = 1.25;

    [RelayCommand]
    private void SetZoom150() => ZoomLevel = 1.5;

    [RelayCommand]
    private void SetZoom200() => ZoomLevel = 2.0;

    [RelayCommand]
    private void SetZoom400() => ZoomLevel = 4.0;

    [RelayCommand]
    private void ZoomIn() => ZoomLevel += ZoomStep;

    [RelayCommand]
    private void ZoomOut() => ZoomLevel -= ZoomStep;

    /// <summary>
    /// Real per-page rail thumbnails (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md
    /// §4), decoded lazily on a background thread rather than eagerly on <see cref="Load"/> - an
    /// eager synchronous decode of up to <see cref="MaxThumbnails"/> pages would be a real, visible
    /// hang on a large issue, exactly what the reader canvas's virtualization principle exists to
    /// avoid. <paramref name="generation"/> guards against a stale background pass from a
    /// previously-open issue clobbering a newer one after the user flips issues quickly.
    /// </summary>
    private void StartThumbnailGeneration(int generation, int thumbnailCount)
    {
        var decoder = _decoder;
        if (decoder is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            for (int page = 0; page < thumbnailCount; page++)
            {
                if (generation != _loadGeneration)
                {
                    return;
                }

                Bitmap? thumb;
                try
                {
                    thumb = decoder.GetThumbnail(page);
                }
                catch
                {
                    thumb = null; // one bad page doesn't break the rest — same contract as GetPage
                }

                // Real bug found in production: `page` is the shared `for`-loop control variable -
                // every closure below was capturing it BY REFERENCE, not by value. The background
                // loop races far ahead of the UI thread draining its dispatcher queue (decoding a
                // 130x200 thumbnail takes microseconds), so by the time a given Post finally ran,
                // `page` had already advanced past whatever value this iteration meant to write -
                // scrambling thumbnails onto later/duplicate tiles and leaving page 0 (which every
                // subsequent iteration raced past before its own Post ever got a turn) permanently
                // unwritten. `capturedPage` gives each closure its own iteration-local snapshot.
                int capturedPage = page;
                Dispatcher.UIThread.Post(() =>
                {
                    if (generation != _loadGeneration || capturedPage >= Thumbnails.Count)
                    {
                        return;
                    }

                    var existing = Thumbnails[capturedPage];
                    Thumbnails[capturedPage] = new ReaderThumbnailSample { CoverBrush = CoverBrush, CoverImage = thumb, IsSelected = existing.IsSelected };
                });
            }
        });
    }

    private void RefreshCurrentPage()
    {
        if (_decoder is null)
        {
            CurrentPage = null;
            return;
        }

        try
        {
            CurrentPage = _decoder.GetPage(_currentPageIndex);
            ErrorMessage = null;
        }
        catch (Exception)
        {
            CurrentPage = null;
            ErrorMessage = $"Couldn't decode page {_currentPageIndex + 1}.";
        }
    }

    private void GoToPage(int pageIndex)
    {
        if (_decoder is null || _loadedIssueId is not int issueId)
        {
            return;
        }

        int pageCount = _decoder.PageCount;
        pageIndex = Math.Clamp(pageIndex, 0, pageCount - 1);
        if (pageIndex == _currentPageIndex)
        {
            return;
        }

        _currentPageIndex = pageIndex;
        PageLabel = $"PAGE {_currentPageIndex + 1} / {pageCount}";
        ProgressFraction = pageCount > 1 ? (double)_currentPageIndex / (pageCount - 1) : 0;
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(ProgressFraction));

        // AppSettings.ResetZoomOnPageChange (docs/superpowers/specs/2026-08-10-preferences-reader-
        // tab-design.md) - CE parity, off by default (matches Paperbunkr's own pre-existing
        // behavior of leaving zoom alone across page turns within a session).
        if (_resetZoomOnPageChange)
        {
            ZoomLevel = 1.0;
        }

        int thumbnailCount = Thumbnails.Count;
        for (int page = 0; page < thumbnailCount; page++)
        {
            var existing = Thumbnails[page];
            Thumbnails[page] = new ReaderThumbnailSample { CoverBrush = CoverBrush, CoverImage = existing.CoverImage, IsSelected = page == _currentPageIndex };
        }

        RefreshCurrentPage();

        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.FirstOrDefault(i => i.Id == issueId);
        if (issue is not null)
        {
            issue.LastPageRead = _currentPageIndex;
            context.SaveChanges();
        }
    }

    /// <summary>
    /// P6 fix (docs/alpha-todo.md) - the thumbnail rail rendered <c>Border.thumb.selected</c>
    /// styling implying click-to-jump, but nothing wired a click to <see cref="GoToPage"/>.
    /// <see cref="Thumbnails"/>' index already *is* the page index (populated by a straight
    /// <c>for</c> loop in <see cref="Load"/>), so this just needs the clicked sample's position.
    /// </summary>
    [RelayCommand]
    private void SelectThumbnail(ReaderThumbnailSample? thumbnail)
    {
        if (thumbnail is null)
        {
            return;
        }

        int index = Thumbnails.IndexOf(thumbnail);
        if (index >= 0)
        {
            GoToPage(index);
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (_currentPageIndex > 0)
        {
            GoToPage(_currentPageIndex - 1);
            return;
        }

        NavigateToAdjacentIssue(forward: false);
    }

    [RelayCommand]
    private void NextPage()
    {
        if (_decoder is not null && _currentPageIndex < _decoder.PageCount - 1)
        {
            GoToPage(_currentPageIndex + 1);
            return;
        }

        NavigateToAdjacentIssue(forward: true);
    }

    /// <summary>
    /// Spatial commands (docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md §3) -
    /// bound to <see cref="Views.PageCanvas.LeftCommand"/>/<see cref="Views.PageCanvas.RightCommand"/>
    /// and the bottom scrubber's ◀/▶ buttons, which are always spatial (left key/click, right
    /// key/click) regardless of reading direction. <see cref="PreviousPage"/>/<see cref="NextPage"/>
    /// themselves keep their plain forward/backward page-index semantics unchanged.
    /// </summary>
    [RelayCommand]
    private void GoLeft()
    {
        if (_isRightToLeft)
        {
            NextPage();
        }
        else
        {
            PreviousPage();
        }
    }

    [RelayCommand]
    private void GoRight()
    {
        if (_isRightToLeft)
        {
            PreviousPage();
        }
        else
        {
            NextPage();
        }
    }

    /// <summary>
    /// "Reading beyond the start or end opens the next Book" (docs/superpowers/specs/
    /// 2026-08-07-preferences-behavior-tab-design.md §3), gated by <c>AutoNavigateComics</c>.
    /// Forward lands on the next issue's first page; backward lands on the previous issue's
    /// *last* page, so backward reading flows continuously instead of restarting each issue.
    /// No-ops at either end of the series, or when the setting is off - same as today's clamp.
    /// </summary>
    private void NavigateToAdjacentIssue(bool forward)
    {
        if (_loadedSeriesId is not int seriesId || _loadedIssueId is not int currentIssueId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        if (!context.GetOrCreateAppSettings().AutoNavigateComics)
        {
            return;
        }

        var series = context.Series.Include(s => s.Issues).FirstOrDefault(s => s.Id == seriesId);
        var orderedIssues = series?.Issues.OrderByNumber().ToList();
        int index = orderedIssues?.FindIndex(i => i.Id == currentIssueId) ?? -1;
        if (series is null || orderedIssues is null || index < 0)
        {
            return;
        }

        int adjacentIndex = forward ? index + 1 : index - 1;
        if (adjacentIndex < 0 || adjacentIndex >= orderedIssues.Count)
        {
            return;
        }

        Load(orderedIssues[adjacentIndex], series, context, forcedStartPage: forward ? 0 : int.MaxValue);
    }

    [RelayCommand]
    private void GoBack() => _goBack();
}
