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
    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            double clamped = Math.Clamp(value, 1.0, 4.0);
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
        PageTurnLeftKey = _keyBindings.GetKey(context, KeyboardCommandRegistry.ReaderPageTurnLeft);
        PageTurnRightKey = _keyBindings.GetKey(context, KeyboardCommandRegistry.ReaderPageTurnRight);
        UpdateReadingModeState(issue.ReadingModeOverride ?? series.ReadingMode, appSettings.ReverseRtlNavigation);

        ErrorMessage = null;
        ZoomLevel = 1.0;
        int pageCount = issue.PageCount is > 0 ? issue.PageCount.Value : 1;

        if (!string.IsNullOrEmpty(issue.FilePath))
        {
            _decoder = PageImageDecoder.TryOpen(issue.FilePath);
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

    /// <summary>Shared by <see cref="Load"/> and <see cref="ToggleReadingMode"/> so the label/spatial-flip switch can't drift apart between the two.</summary>
    private void UpdateReadingModeState(ReadingMode effectiveMode, bool reverseRtlNavigation)
    {
        _isRightToLeft = effectiveMode == ReadingMode.RightToLeft && reverseRtlNavigation;
        ReadingModeLabel = effectiveMode switch
        {
            ReadingMode.RightToLeft => "Right to Left ▾",
            ReadingMode.VerticalContinuous => "Vertical (Continuous) ▾",
            ReadingMode.HorizontalContinuous => "Horizontal (Continuous) ▾",
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

                Dispatcher.UIThread.Post(() =>
                {
                    if (generation != _loadGeneration || page >= Thumbnails.Count)
                    {
                        return;
                    }

                    var existing = Thumbnails[page];
                    Thumbnails[page] = new ReaderThumbnailSample { CoverBrush = CoverBrush, CoverImage = thumb, IsSelected = existing.IsSelected };
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
