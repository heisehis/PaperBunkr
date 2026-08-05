using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Reader screen, ported from ReaderScreen.dc.html (Claude Design project 43c40b25). Loads a
/// real <see cref="Issue"/> (via <see cref="LoadIssue"/>/<see cref="EnsureIssueLoaded"/>) instead
/// of the wireframe's hardcoded "Brass Horizon #12" sample content. There is still no real page
/// decode/rendering pipeline (docs/onboarding.md §8 - virtualized bitmap decode, zoom/pan - is
/// separate, harder work); the center canvas still shows a placeholder tile, just with the real
/// page number/series/issue identity now, and the thumbnail rail/progress reflect the issue's
/// real page count and read position instead of static numbers.
/// </summary>
public partial class ReaderScreenViewModel : ViewModelBase
{
    // A single issue's page thumbnails aren't virtualized yet (§8) - fine for ordinary comic/
    // manga chapter lengths, but bounded so a pathological PageCount can't blow up the UI.
    private const int MaxThumbnails = 200;

    private readonly Action _goBack;
    private int? _loadedIssueId;

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
    public string PageNumber { get; private set; } = string.Empty;
    public string PageSubtitle { get; private set; } = string.Empty;
    public double ProgressFraction { get; private set; }

    /// <summary>Loads a specific issue by id (e.g. from Detail's Continue button).</summary>
    public void LoadIssue(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Include(i => i.Series).FirstOrDefault(i => i.Id == issueId);
        if (issue?.Series is null)
        {
            return;
        }

        Load(issue, issue.Series);
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
            Load(issue, series);
        }
    }

    private void Load(Issue issue, Series series)
    {
        _loadedIssueId = issue.Id;

        CoverBrush = SeriesCardSample.CoverBrushFor(series.Name);
        BreadcrumbSeries = $"Library / {series.Name} /";
        IssueTitle = string.IsNullOrWhiteSpace(issue.Title)
            ? $"Issue #{issue.Number}"
            : $"Issue #{issue.Number} — {issue.Title}";

        var readingMode = issue.ReadingModeOverride ?? series.ReadingMode;
        ReadingModeLabel = readingMode switch
        {
            ReadingMode.RightToLeft => "Right to Left ▾",
            ReadingMode.VerticalContinuous => "Vertical (Continuous) ▾",
            ReadingMode.HorizontalContinuous => "Horizontal (Continuous) ▾",
            _ => "Left to Right ▾",
        };

        int pageCount = issue.PageCount is > 0 ? issue.PageCount.Value : 1;
        int currentPage = Math.Clamp((issue.LastPageRead ?? 0) + 1, 1, pageCount);

        PageLabel = $"PAGE {currentPage} / {pageCount}";
        PageNumber = currentPage.ToString();
        PageSubtitle = $"{series.Name} · #{issue.Number}";
        ProgressFraction = (double)currentPage / pageCount;

        Thumbnails.Clear();
        int thumbnailCount = Math.Min(pageCount, MaxThumbnails);
        for (int page = 1; page <= thumbnailCount; page++)
        {
            Thumbnails.Add(new ReaderThumbnailSample { CoverBrush = CoverBrush, IsSelected = page == currentPage });
        }

        OnPropertyChanged(nameof(CoverBrush));
        OnPropertyChanged(nameof(BreadcrumbSeries));
        OnPropertyChanged(nameof(IssueTitle));
        OnPropertyChanged(nameof(ReadingModeLabel));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(PageNumber));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(ProgressFraction));
    }

    [RelayCommand]
    private void GoBack() => _goBack();
}
