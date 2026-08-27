using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Dedicated detail screen for Manga/Manhua/Manhwa series (docs/superpowers/specs/2026-08-23-
/// manga-detail-screen-design.md, Stage 6 of docs/tracker-manga-ui-research.md) - a denser,
/// chapter-list-first presentation distinct from the Western <see cref="DetailScreenViewModel"/>'s
/// cover-tile Issues tab. Chapters get a list-row layout rather than cover tiles deliberately:
/// unlike Western comic issues, manga chapters don't carry variant covers, so a tile grid would add
/// no information a text row doesn't already carry.
///
/// <see cref="Tabs"/> is the same <see cref="DetailTabsViewModel"/> the Western screen uses,
/// embedded here with its own Issues tab suppressed (<see cref="DetailTabsViewModel.ShowIssuesTab"/>)
/// - Related/Details(tracker linking + external metadata)/Activity are shared, not duplicated.
/// </summary>
public partial class MangaDetailScreenViewModel : ViewModelBase
{
    public MangaDetailScreenViewModel(Action goBack, Action<int> goToReader, Action<int> goToProperties, Action<IReadOnlyList<int>> goToBulkProperties, Action<int>? goDetailForSeries = null, Action<string>? goLibraryWithSearch = null)
    {
        _goBack = goBack;
        _goToReader = goToReader;
        _goToProperties = goToProperties;
        _goToBulkProperties = goToBulkProperties;
        _goDetailForSeries = goDetailForSeries ?? (_ => { });
        Tabs = new DetailTabsViewModel(goToProperties, goToBulkProperties) { ShowIssuesTab = false, ShowTabStrip = false };
        // No reweight callback - LoadSeries below is always the series-aggregated view (chapter-list
        // screen, no single-issue pill focus like the Western DetailScreenViewModel has), so every
        // pill's CanReweight is naturally false here regardless.
        Pills = new DetailPillsViewModel(goLibraryWithSearch);
        Chapters = new ObservableCollection<ChapterRowSample>();
        ExternalLinks = new ObservableCollection<ExternalLinkSample>();
    }

    private readonly Action _goBack;
    private readonly Action<int> _goToReader;
    private readonly Action<int> _goToProperties;
    private readonly Action<IReadOnlyList<int>> _goToBulkProperties;
    private readonly Action<int> _goDetailForSeries;
    private int? _seriesId;
    private int? _continueIssueId;
    private int? _coverIssueId;
    private bool _isLoadingSeries;
    private List<ChapterRowSample> _allChapters = new();

    public DetailTabsViewModel Tabs { get; }
    public DetailPillsViewModel Pills { get; }
    public ObservableCollection<ChapterRowSample> Chapters { get; }

    /// <summary>Header chip row (docs/superpowers/specs/2026-08-23-apply-from-provider-design.md) -
    /// same data <c>DetailTabsViewModel.ExternalLinks</c> already shows in the Details tab, promoted
    /// to the header too. Flat, not grouped by "purpose" the way MangaBaka's own Info tab does -
    /// every provider in <see cref="ExternalMetadataProvider"/> is a metadata/tracker site in this
    /// app's model, so a purpose grouping would have exactly one populated bucket.</summary>
    public ObservableCollection<ExternalLinkSample> ExternalLinks { get; }

    public bool HasExternalLinks => ExternalLinks.Count > 0;

    /// <summary>Same reclassify picker as <see cref="DetailScreenViewModel.ContentTypeOptions"/> - see that property's own doc comment.</summary>
    public ContentType[] ContentTypeOptions { get; } = Enum.GetValues<ContentType>();

    [ObservableProperty]
    private Bitmap? _coverImage;

    /// <summary>Pre-rendered blurred backdrop - see <see cref="BackdropBlurRenderer"/>'s own doc
    /// comment for why this isn't a live Avalonia Effect in the header's XAML.</summary>
    [ObservableProperty]
    private Bitmap? _backdropImage;

    [ObservableProperty]
    private string _seriesTitle = string.Empty;

    [ObservableProperty]
    private ContentType _selectedContentType;

    /// <summary>See <see cref="DetailScreenViewModel.OnSelectedContentTypeChanged"/>'s own doc comment - identical rerouting behavior, mirrored here.</summary>
    partial void OnSelectedContentTypeChanged(ContentType value)
    {
        if (_isLoadingSeries || _seriesId is not int seriesId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Find(seriesId);
        if (series is null)
        {
            return;
        }

        series.ContentType = value;
        context.SaveChanges();
        _goDetailForSeries(seriesId);
    }

    [ObservableProperty]
    private string _statusLabel = string.Empty;

    /// <summary>"via MangaBaka" / "via AniList" caption (docs/superpowers/specs/2026-08-23-apply-
    /// from-provider-design.md) - null (not blank) when <see cref="StatusLabel"/> was never
    /// provider-sourced (manual entry or CE migration), so the caption is simply absent.</summary>
    [ObservableProperty]
    private string? _statusAttribution;

    [ObservableProperty]
    private string _sourceLabel = "Unknown";

    [ObservableProperty]
    private string _readingModeBadge = "LTR";

    /// <summary>Chapter/volume count badges (docs/superpowers/specs/2026-08-23-apply-from-provider-
    /// design.md's Meta-badge-row polish) - both already computable from data <see cref="LoadSeries"/>
    /// loads anyway, just not previously surfaced as badges.</summary>
    [ObservableProperty]
    private string _chapterCountBadge = string.Empty;

    [ObservableProperty]
    private string _volumeCountBadge = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Same treatment as <see cref="StatusAttribution"/>, for <see cref="Summary"/>.</summary>
    [ObservableProperty]
    private string? _summaryAttribution;

    [ObservableProperty]
    private bool _isSynopsisExpanded;

    public string SynopsisToggleLabel => IsSynopsisExpanded ? "Show less ▲" : "Show more ▼";

    partial void OnIsSynopsisExpandedChanged(bool value) => OnPropertyChanged(nameof(SynopsisToggleLabel));

    [RelayCommand]
    private void ToggleSynopsis() => IsSynopsisExpanded = !IsSynopsisExpanded;

    [ObservableProperty]
    private string _continueLabel = "Start Reading";

    public bool CanEdit => _continueIssueId is not null;

    [ObservableProperty]
    private ChapterListFilter _chapterFilter = ChapterListFilter.All;

    [ObservableProperty]
    private ChapterSortField _chapterSortField = ChapterSortField.Number;

    [ObservableProperty]
    private SortDirection _chapterSortDirection = SortDirection.Ascending;

    public string ChapterSortDirectionGlyph => ChapterSortDirection == SortDirection.Ascending ? "↑" : "↓";

    public bool HasChapters => Chapters.Count > 0;

    partial void OnChapterFilterChanged(ChapterListFilter value) => RenderChapters();

    partial void OnChapterSortFieldChanged(ChapterSortField value) => RenderChapters();

    partial void OnChapterSortDirectionChanged(SortDirection value)
    {
        OnPropertyChanged(nameof(ChapterSortDirectionGlyph));
        RenderChapters();
    }

    [RelayCommand]
    private void SetChapterFilter(ChapterListFilter filter) => ChapterFilter = filter;

    [RelayCommand]
    private void SetChapterSortField(ChapterSortField field) => ChapterSortField = field;

    [RelayCommand]
    private void ToggleChapterSortDirection() =>
        ChapterSortDirection = ChapterSortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;

    /// <summary>Loads the series with the given id and refreshes every bound field - same shape as <see cref="DetailScreenViewModel.LoadSeries"/>.</summary>
    public void LoadSeries(int seriesId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Include(s => s.Issues).ThenInclude(i => i.MetadataProposals)
            .Include(s => s.Issues).ThenInclude(i => i.Tags)
            .Include(s => s.MetadataProposals)
            .FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return;
        }

        _isLoadingSeries = true;
        _seriesId = seriesId;

        var card = SeriesCardSample.FromSeries(series);
        _coverIssueId = card.CoverIssueId;
        CoverImage = card.CoverIssueId is int coverIssueId ? CoverImageCache.Get(coverIssueId) : null;
        BackdropImage = CoverImage is not null ? BackdropBlurRenderer.Render(CoverImage, new PixelSize(1600, 420)) : null;
        SeriesTitle = series.Name;
        SelectedContentType = series.ContentType;
        StatusLabel = series.Status switch
        {
            SeriesStatus.Ongoing => "Ongoing",
            SeriesStatus.Completed => "Completed",
            SeriesStatus.Cancelled => "Cancelled",
            SeriesStatus.Hiatus => "Hiatus",
            _ => "Unknown",
        };
        StatusAttribution = AttributionLabel(series, MetadataProposalField.Status);
        ReadingModeBadge = series.ReadingMode switch
        {
            ReadingMode.RightToLeft or ReadingMode.HorizontalContinuousRightToLeft => "RTL",
            ReadingMode.TopToBottom or ReadingMode.VerticalContinuous or ReadingMode.HorizontalContinuous => "Vertical",
            ReadingMode.Webtoon => "Webtoon",
            _ => "LTR",
        };
        Summary = string.IsNullOrWhiteSpace(series.Summary) ? "No summary available." : series.Summary;
        SummaryAttribution = AttributionLabel(series, MetadataProposalField.Summary);
        IsSynopsisExpanded = false;

        RefreshExternalLinks(context, seriesId);

        // "Unknown" for both no scan-source data and a mix of sources across issues, per this
        // screen's design doc - a single distinct value is the only case worth naming.
        var distinctScanInfo = series.Issues
            .Select(i => i.ScanInformation)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SourceLabel = distinctScanInfo.Count == 1 ? distinctScanInfo[0]! : "Unknown";

        // Same Continue-pick priority as DetailScreenViewModel.LoadSeries - see that method's own
        // doc comment for the "in progress beats never-opened beats re-read" rationale.
        var inProgress = series.Issues
            .Where(i => i.LastPageRead is > 0 && i.PageCount is > 0 && i.LastPageRead < i.PageCount)
            .OrderByNumber()
            .FirstOrDefault();
        var nextUnread = series.Issues
            .Where(i => i.LastPageRead is null or 0)
            .OrderByNumber()
            .FirstOrDefault();
        var continueIssue = inProgress ?? nextUnread;
        bool isReread = continueIssue is null;
        continueIssue ??= series.Issues.OrderByNumber().FirstOrDefault();

        _continueIssueId = continueIssue?.Id;
        ContinueLabel = continueIssue is null
            ? "No Chapters"
            : isReread
                ? $"Re-read — {continueIssue.EffectiveNumber()}"
                : $"Continue — {continueIssue.EffectiveNumber()}";

        _allChapters = series.Issues.OrderByNumber().Select(ToChapterRow).ToList();
        ChapterFilter = ChapterListFilter.All;
        RenderChapters();
        MangaActiveTab = "chapters";

        ChapterCountBadge = _allChapters.Count == 1 ? "1 chapter" : $"{_allChapters.Count} chapters";
        int volumeCount = series.Issues.Select(i => i.EffectiveVolume()).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Count();
        VolumeCountBadge = volumeCount switch
        {
            0 => string.Empty,
            1 => "1 volume",
            _ => $"{volumeCount} volumes",
        };

        var enabledVirtualTags = context.VirtualTagDefinitions.Where(t => t.IsEnabled).OrderBy(t => t.SortOrder).ToList();
        Tabs.LoadSeries(series);
        Pills.LoadSeries(series, enabledVirtualTags);

        _isLoadingSeries = false;
        OnPropertyChanged(nameof(CanEdit));
    }

    private static ChapterRowSample ToChapterRow(Issue issue) => new()
    {
        Id = issue.Id,
        DisplayNumber = string.IsNullOrWhiteSpace(issue.EffectiveNumber()) ? "#?" : $"#{issue.EffectiveNumber()}",
        NumberSortKey = issue.NumberSortKey(),
        Title = issue.EffectiveTitle(),
        IsRead = issue.HasBeenRead(),
        IsInProgress = issue.IsInProgress(),
        ReadPercentage = issue.ReadPercentage(),
        BookmarkCount = issue.Bookmarks.Count,
        IsMissing = issue.FileIsMissing,
        ScanInformation = issue.ScanInformation,
        Date = issue.ReleasedTime ?? issue.AddedTime,
    };

    private void RenderChapters()
    {
        IEnumerable<ChapterRowSample> filtered = ChapterFilter switch
        {
            ChapterListFilter.Unread => _allChapters.Where(c => !c.IsRead),
            ChapterListFilter.Bookmarked => _allChapters.Where(c => c.HasBookmark),
            ChapterListFilter.Missing => _allChapters.Where(c => c.IsMissing),
            _ => _allChapters,
        };

        var sorted = ChapterSortField == ChapterSortField.Date
            ? (ChapterSortDirection == SortDirection.Ascending ? filtered.OrderBy(c => c.Date) : filtered.OrderByDescending(c => c.Date))
            : (ChapterSortDirection == SortDirection.Ascending ? filtered.OrderBy(c => c.NumberSortKey) : filtered.OrderByDescending(c => c.NumberSortKey));

        Chapters.Clear();
        foreach (var row in sorted)
        {
            Chapters.Add(row);
        }

        OnPropertyChanged(nameof(HasChapters));
    }

    // --- Unified tab strip (Chapters/Related/Details/Activity) - this screen renders its own
    // single tab-strip row rather than showing the embedded DetailTabsViewModel's own strip
    // underneath it (see DetailTabsViewModel.ShowTabStrip's doc comment). Related/Details/Activity
    // clicks also drive Tabs' own ActiveTab via its Go*Command, so its content sections' own
    // IsRelatedTab/IsDetailsTab/IsActivityTab visibility bindings keep working unchanged. ---

    [ObservableProperty]
    private string _mangaActiveTab = "chapters";

    public bool IsChaptersTab => MangaActiveTab == "chapters";
    public bool IsRelatedTabActive => MangaActiveTab == "related";
    public bool IsDetailsTabActive => MangaActiveTab == "details";
    public bool IsActivityTabActive => MangaActiveTab == "activity";

    partial void OnMangaActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsChaptersTab));
        OnPropertyChanged(nameof(IsRelatedTabActive));
        OnPropertyChanged(nameof(IsDetailsTabActive));
        OnPropertyChanged(nameof(IsActivityTabActive));
    }

    [RelayCommand]
    private void GoChaptersTab() => MangaActiveTab = "chapters";

    [RelayCommand]
    private void GoRelatedTab()
    {
        MangaActiveTab = "related";
        Tabs.GoRelatedCommand.Execute(null);
    }

    [RelayCommand]
    private void GoDetailsTab()
    {
        MangaActiveTab = "details";
        Tabs.GoDetailsCommand.Execute(null);
    }

    [RelayCommand]
    private void GoActivityTab()
    {
        MangaActiveTab = "activity";
        Tabs.GoActivityCommand.Execute(null);
    }

    [RelayCommand]
    private void GoBack() => _goBack();

    [RelayCommand]
    private void Continue()
    {
        if (_continueIssueId is int issueId)
        {
            _goToReader(issueId);
        }
    }

    /// <summary>Header action-row Edit button - targets the same issue Continue Reading would open
    /// (docs/superpowers/specs/2026-08-23-manga-detail-screen-design.md), since the Chapters tab has
    /// no tile-multi-select the way the Western Issues tab does. Any other specific chapter is
    /// reachable via <see cref="EditChapterProperties"/>'s per-row context menu.</summary>
    [RelayCommand]
    private void Edit()
    {
        if (_continueIssueId is int issueId)
        {
            _goToProperties(issueId);
        }
    }

    /// <summary>Row click opens the reader directly at that chapter (docs/superpowers/specs/
    /// 2026-08-23-manga-detail-screen-design.md, per user direction) - reuses the same
    /// <c>Action&lt;int&gt;</c> hand-off <see cref="IssueListScreenViewModel.OpenIssue"/> uses.</summary>
    [RelayCommand]
    private void OpenChapter(ChapterRowSample? row)
    {
        if (row is not null)
        {
            _goToReader(row.Id);
        }
    }

    [RelayCommand]
    private void EditChapterProperties(ChapterRowSample? row)
    {
        if (row is not null)
        {
            _goToProperties(row.Id);
        }
    }

    [RelayCommand]
    private void RevealChapter(ChapterRowSample? row)
    {
        if (row is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(row.Id);
        if (issue is not null)
        {
            RevealInExplorerHelper.RevealIssue(issue);
        }
    }

    /// <summary>Chapter row context menu's "Mark as Read"/"Mark as Unread" (docs/superpowers/specs/2026-08-23-mark-as-read-design.md) - single-item only, this screen's chapter rows have no selection model to bulk-apply over.</summary>
    [RelayCommand]
    private void MarkChapterRead(ChapterRowSample? row) => MarkChapterReadState(row, IssueReadStateResolver.MarkAsRead);

    [RelayCommand]
    private void MarkChapterUnread(ChapterRowSample? row) => MarkChapterReadState(row, IssueReadStateResolver.MarkAsUnread);

    private void MarkChapterReadState(ChapterRowSample? row, Action<Issue> apply)
    {
        if (row is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(row.Id);
        if (issue is null)
        {
            return;
        }

        apply(issue);
        context.SaveChanges();
        ReloadCurrentSeries();
    }

    // --- Cover art override (docs/superpowers/specs/2026-08-23-cover-art-override-design.md) -
    // deliberate CE deviation: overrides any cover, linked file or not. Both the header's "Cover"
    // action button (targets the series' designated cover issue) and each chapter row's context
    // menu (targets that specific chapter) funnel through the same ChangeCover/ResetCover pair. ---

    [RelayCommand]
    private async Task ChangeCoverAsync(int issueId)
    {
        string? path = await new FilePickerService().PickImageFileAsync("Choose Cover Image");
        if (path is null)
        {
            return;
        }

        if (new CoverThumbnailService().TrySetCustomCover(issueId, path))
        {
            ReloadCurrentSeries();
        }
    }

    [RelayCommand]
    private void ResetCover(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        string? filePath = context.Issues.Find(issueId)?.FilePath;
        new CoverThumbnailService().ResetCover(issueId, filePath);
        ReloadCurrentSeries();
    }

    [RelayCommand]
    private Task ChangeSeriesCoverAsync() => _coverIssueId is int issueId ? ChangeCoverAsync(issueId) : Task.CompletedTask;

    [RelayCommand]
    private async Task ChangeChapterCoverAsync(ChapterRowSample? row)
    {
        if (row is not null)
        {
            await ChangeCoverAsync(row.Id);
        }
    }

    [RelayCommand]
    private void ResetChapterCover(ChapterRowSample? row)
    {
        if (row is not null)
        {
            ResetCover(row.Id);
        }
    }

    // --- Apply-from-Provider header additions (docs/superpowers/specs/2026-08-23-apply-from-
    // provider-design.md) - attribution captions + external-links chip row. ---

    /// <summary>"via {Provider}" for the most recently accepted Series-scoped proposal on
    /// <paramref name="field"/>, or null when that field was never provider-sourced. Reads from the
    /// already-loaded <c>series.MetadataProposals</c> collection, no separate query - same
    /// established pattern as <c>IssueMetadataExtensions</c>'s own resolvers.</summary>
    private static string? AttributionLabel(Series series, MetadataProposalField field)
    {
        var latest = series.MetadataProposals
            .Where(p => p.Field == field && p.Status == MetadataProposalStatus.Accepted && p.ProviderKey is not null)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();
        return latest is null ? null : $"via {latest.ProviderKey}";
    }

    private void RefreshExternalLinks(PaperbunkrDbContext context, int seriesId)
    {
        ExternalLinks.Clear();
        foreach (var link in ExternalMetadataResolver.GetExternalIds(context, seriesId))
        {
            ExternalLinks.Add(new ExternalLinkSample
            {
                ProviderLabel = link.Provider.ToString(),
                ExternalId = link.ExternalId,
                Url = link.Url,
            });
        }

        OnPropertyChanged(nameof(HasExternalLinks));
    }

    /// <summary>Re-runs <see cref="LoadSeries"/> for whichever series is currently loaded - same
    /// shape as <see cref="DetailScreenViewModel.ReloadCurrentSeries"/>, needed here so a cover
    /// change is visible immediately without a full navigation round-trip.</summary>
    public void ReloadCurrentSeries()
    {
        if (_seriesId is int seriesId)
        {
            LoadSeries(seriesId);
        }
    }
}
