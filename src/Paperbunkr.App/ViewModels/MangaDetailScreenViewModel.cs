using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ContextMenus;
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
public partial class MangaDetailScreenViewModel : ViewModelBase, IDetailHeaderSource, IContextMenuProvider, IListLayoutHost
{
    /// <summary>Right-click/Menu-key menu for the chapter list (docs/superpowers/specs/2026-08-31-
    /// keyboard-operability-design.md) - delegated out, same pattern as
    /// <see cref="LibraryScreenViewModel"/>'s own <see cref="IContextMenuProvider"/> implementation.</summary>
    IReadOnlyList<ContextMenuEntry>? IContextMenuProvider.BuildContextMenu(object? target) =>
        new MangaDetailContextMenuBuilder(this).Build(target);

    public MangaDetailScreenViewModel(Action goBack, Action<int> goToReader, Action<int> goToProperties, Action<IReadOnlyList<int>> goToBulkProperties, Action<int>? goDetailForSeries = null, Action<string>? goLibraryWithSearch = null, Action<int>? goLibraryWithCollection = null, Action<int>? enqueueMetadataWriteBack = null, ITrackerAutoSyncService? trackerAutoSync = null, Action<int>? editSeries = null)
    {
        _editSeries = editSeries;
        _trackerAutoSync = trackerAutoSync ?? NoOpTrackerAutoSyncService.Instance;
        _goBack = goBack;
        _goToReader = goToReader;
        _goToProperties = goToProperties;
        _goToBulkProperties = goToBulkProperties;
        _goDetailForSeries = goDetailForSeries ?? (_ => { });
        _enqueueMetadataWriteBack = enqueueMetadataWriteBack;
        Tabs = new DetailTabsViewModel(goToProperties, goToBulkProperties, navigateToSeries: _goDetailForSeries, openInReader: goToReader, navigateToCollection: goLibraryWithCollection, goLibraryWithSearch: goLibraryWithSearch, trackerAutoSync: trackerAutoSync, onSeriesCoverChanged: ReloadCurrentSeries) { ShowIssuesTab = false, ShowTabStrip = false, IsMangaDetailHost = true };
        // A provider link on a confirmed series changes chapters (tags, summary fallback): queue the ones now owing a write.
        Tabs.SeriesMetadataLinked = EnqueueDirtyChapters;
        Tabs.ReadingModeChanged = mode => ReadingModeBadge = ReadingModeCatalog.BadgeFor(mode);
        // No reweight callback - LoadSeries below is always the series-aggregated view (chapter-list
        // screen, no single-issue pill focus like the Western DetailScreenViewModel has), so every
        // chip's CanReweight is naturally false here regardless.
        Band = new DetailBandViewModel(goLibraryWithSearch, () => Tabs.GoDetailsCommand.Execute(null));
        CoverBrush = SeriesCardSample.Gradient("#26333a", "#7aa7b8");
        Chapters = new ObservableCollection<ChapterRowSample>();
        ChapterGroups = new ObservableCollection<ChapterVolumeGroup>();
        ExternalLinks = new ObservableCollection<ExternalLinkSample>();
    }

    private readonly Action _goBack;
    private readonly Action<int> _goToReader;
    private readonly Action<int> _goToProperties;
    private readonly Action<IReadOnlyList<int>> _goToBulkProperties;
    private readonly Action<int> _goDetailForSeries;
    private readonly Action<int>? _enqueueMetadataWriteBack;
    private readonly Action<int>? _editSeries;
    private int? _seriesId;
    private int? _continueIssueId;
    private int? _coverIssueId;
    private readonly ITrackerAutoSyncService _trackerAutoSync;
    private bool _isLoadingSeries;
    private List<ChapterRowSample> _allChapters = new();
    private DetailListLayoutState _detailLayoutState = DetailListLayoutState.MangaDefault;

    // --- Detail layout (docs/superpowers/specs/2026-10-19-detail-layouts-v2-design.md): one default for every manga series unless a
    // series has its own layout (Library right-click menu); on-screen changes land wherever the series' layout lives. ---

    private bool _seriesHasOwnLayout;
    private bool _applyingLayout;
    private IReadOnlyList<VirtualTagDefinition> _virtualTags = Array.Empty<VirtualTagDefinition>();

    public WorkspaceScreen LayoutScreen => WorkspaceScreen.MangaDetail;
    public string LayoutSelectionKey => _seriesHasOwnLayout && _seriesId is int id ? ListLayoutsViewModel.SeriesKey(id) : ListLayoutAssignment.DefaultKey;
    public bool UsesSingleDefault => true;
    public string CaptureLayoutJson() => DetailListLayoutStateJson.Serialize(_detailLayoutState);
    public string BuiltInDefaultLayoutJson => DetailListLayoutStateJson.Serialize(DetailListLayoutState.MangaDefault);
    /// <summary>The manga chapter layouts. Their menu lives in the Library's Workspace popup, beside the Library's own list layouts, not on this screen.</summary>
    public ListLayoutsViewModel ListLayouts { get; private set; } = null!;

    /// <summary>The chapter table's header row, one cell per visible column - resize and reorder drags act on these.</summary>
    public ObservableCollection<DetailListHeaderCell> ChapterHeaderCells { get; } = new();

    public void InitializeListLayouts(ListLayoutService? service)
    {
        ListLayouts = new ListLayoutsViewModel(this, service, post: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        ListLayouts.AssignmentsChanged += ReapplyLayoutForSeries;
        if (ListLayouts.Initialize() is { } json)
            ListLayouts.ApplyWithoutTracking(() => ApplyLayoutJson(json));
        ListLayouts.MarkApplied();
    }

    /// <summary>Re-reads which layout the shown series uses (its own, else the default) and shows it.</summary>
    private void ReapplyLayoutForSeries()
    {
        if (ListLayouts is null)
        {
            return;
        }

        _seriesHasOwnLayout = _seriesId is int id && ListLayouts.HasAssignment(ListLayoutsViewModel.SeriesKey(id));
        if (ListLayouts.ResolveForSelection() is { } json)
        {
            ListLayouts.ApplyWithoutTracking(() => ApplyLayoutJson(json));
        }
    }

    public DetailListOptionsViewModel CreateDetailListOptions(Action close) =>
        DetailListOptionsViewModel.ForManga(_detailLayoutState, _virtualTags, state =>
        {
            ApplyState(state);
            ListLayouts.TrackChange();
        }, close);

    public void ApplyLayoutJson(string json) => ApplyState(DetailListLayoutStateJson.Deserialize(json, manga: true));

    private void ApplyState(DetailListLayoutState state)
    {
        _detailLayoutState = state;
        _applyingLayout = true;
        try
        {
            ChapterSortBy = state.ChapterSortBy;
            ChapterSortDirection = state.ChapterSortDirection;
        }
        finally
        {
            _applyingLayout = false;
        }

        RenderChapters();
    }

    /// <summary>Header drag: a column's new width, committed on release.</summary>
    public void SetChapterColumnWidth(string field, double width)
    {
        var columns = (_detailLayoutState.ChapterColumns ?? DetailListLayoutState.MangaDefault.ChapterColumns!)
            .Select(c => c.Field == field ? c with { Width = width } : c).ToList();
        _detailLayoutState = DetailListLayoutStateJson.Normalize(_detailLayoutState with { ChapterColumns = columns }, manga: true);
        RenderChapters();
        ListLayouts?.TrackChange();
    }

    /// <summary>Header drag: moves a column in front of (dragging left) or behind (dragging right) another one.</summary>
    public void MoveChapterColumn(string field, string targetField)
    {
        _detailLayoutState = _detailLayoutState with
        {
            ChapterColumns = DetailColumnMoves.Move(_detailLayoutState.ChapterColumns ?? DetailListLayoutState.MangaDefault.ChapterColumns!, field, targetField),
        };
        RenderChapters();
        ListLayouts?.TrackChange();
    }

    public DetailTabsViewModel Tabs { get; }

    /// <summary>Where Reveal in Explorer reports a file that's no longer on disk. Set by <see cref="MainViewModel"/> after construction; null (tests) = no alert.</summary>
    public IActivityService? ActivityCenter { get; set; }
    public DetailBandViewModel Band { get; }
    public ObservableCollection<ChapterRowSample> Chapters { get; }

    /// <summary>Chapters grouped by volume for the release-feed presentation (docs/superpowers/specs/
    /// 2026-08-28-detail-screens-streaming-redesign-design.md). <see cref="Chapters"/> stays flat as
    /// the data source; this is the view projection, rebuilt whenever <see cref="RenderChapters"/> runs.</summary>
    public ObservableCollection<ChapterVolumeGroup> ChapterGroups { get; }

    // --- IDetailHeaderSource ---

    [ObservableProperty]
    private IBrush _coverBrush;

    public string HeaderTitle => SeriesTitle;

    partial void OnSeriesTitleChanged(string value) => OnPropertyChanged(nameof(HeaderTitle));

    [ObservableProperty]
    private string? _secondaryTitle;

    [ObservableProperty]
    private string _metaLine = string.Empty;

    DetailHeroProgress? IDetailHeaderSource.TrackerProgress => null;
    string? IDetailHeaderSource.SharedElementKey => _seriesId is int seriesId ? $"series-cover:{seriesId}" : null;

    private string? _readingStatus;
    private ReadingStatusPickerViewModel? _readingStatusPicker;
    private DetailMetaBadgeGroup _metaBadges = new(System.Array.Empty<DetailMetaBadge>());
    private string? _issueSummaryLine;
    string? IDetailHeaderSource.ReadingStatus => _readingStatus;
    ReadingStatusPickerViewModel? IDetailHeaderSource.ReadingStatusPicker => _readingStatusPicker;
    DetailMetaBadgeGroup IDetailHeaderSource.MetaBadges => _metaBadges;
    string? IDetailHeaderSource.IssueSummaryLine => _issueSummaryLine;

    private void OnReadingStatusPicked()
    {
        _readingStatus = _readingStatusPicker?.CurrentValue;
        OnPropertyChanged(nameof(IDetailHeaderSource.ReadingStatus));
        Band.ReadingStatusValue = _readingStatus;
    }

    public IReadOnlyList<DetailHeroAction> Actions => new[]
    {
        new DetailHeroAction(ContinueLabel, ContinueCommand, IsPrimary: true, IsEnabled: _continueIssueId is not null, Icon: Symbol.Play),
        new DetailHeroAction("Edit Series", EditSeriesCommand, IsEnabled: CanEdit, Icon: Symbol.Edit),
        new DetailHeroAction("Change Cover", Command: null, Icon: Symbol.Image, FlyoutContext: BuildCoverPickerViewModel()),
    }.Where(a => !IsRemoteSeries || a.Label != "Change Cover").ToArray();   // a remote series' cover isn't ours to change

    /// <summary>Same shape as <see cref="DetailScreenViewModel.BuildCoverPickerViewModel"/> - see its doc comment.</summary>
    private CoverPickerViewModel? BuildCoverPickerViewModel() =>
        !IsRemoteSeries && _seriesId is int seriesId ? new CoverPickerViewModel(seriesId, ReloadCurrentSeries, FindMangaBakaCoverContext()) : null;

    /// <summary>Same shape as <see cref="DetailScreenViewModel.FindMangaBakaCoverContext"/> - see its doc comment.</summary>
    private (ExternalMetadataProvider Provider, string ExternalId)? FindMangaBakaCoverContext()
    {
        if (_seriesId is not int seriesId)
        {
            return null;
        }

        using var context = PaperbunkrDb.CreateContext();
        var link = ExternalMetadataResolver.GetExternalIds(context, seriesId)
            .FirstOrDefault(e => e.Provider == ExternalMetadataProvider.MangaBaka);

        return link is null ? null : (link.Provider, link.ExternalId);
    }

    /// <summary>Header chip row (docs/superpowers/specs/2026-08-23-apply-from-provider-design.md) -
    /// same data <c>DetailTabsViewModel.ExternalLinks</c> already shows in the Details tab, promoted
    /// to the header too. Flat, not grouped by "purpose" the way MangaBaka's own Info tab does -
    /// every provider in <see cref="ExternalMetadataProvider"/> is a metadata/tracker site in this
    /// app's model, so a purpose grouping would have exactly one populated bucket.</summary>
    public ObservableCollection<ExternalLinkSample> ExternalLinks { get; }

    public bool HasExternalLinks => ExternalLinks.Count > 0;

    /// <summary>Same reclassify picker as <see cref="DetailScreenViewModel.ContentTypeOptions"/> - see that property's own doc comment.</summary>
    public ContentType[] ContentTypeOptions { get; } = Enum.GetValues<ContentType>();

    /// <summary>String projection for the <c>SuggestBox</c> reclassify picker - see
    /// <see cref="DetailScreenViewModel.SelectedContentTypeText"/>.</summary>
    public string SelectedContentTypeText
    {
        get => SelectedContentType.ToString();
        set { if (Enum.TryParse<ContentType>(value, out var parsed)) SelectedContentType = parsed; }
    }

    public string[] ContentTypeNames { get; } = Enum.GetNames<ContentType>();

    [ObservableProperty]
    private Bitmap? _coverImage;

    /// <summary>Pre-rendered blurred backdrop - see <see cref="BackdropBlurRenderer"/>'s own doc
    /// comment for why this isn't a live Avalonia Effect in the header's XAML.</summary>
    [ObservableProperty]
    private Bitmap? _backdropImage;

    [ObservableProperty]
    private string _seriesTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedContentTypeText))]
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

        SeriesContentTypeEditor.SetManual(series, value);
        context.SaveChanges();

        // Content type drives the ComicInfo <Manga> field for every issue in the series
        // (docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md).
        if (_enqueueMetadataWriteBack is not null)
        {
            foreach (int issueId in context.Issues.Where(i => i.SeriesId == seriesId).Select(i => i.Id).ToList())
            {
                _enqueueMetadataWriteBack(issueId);
            }
        }

        _goDetailForSeries(seriesId);
    }

    /// <summary>
    /// True while the series' type was applied on its own by the tracker classifier and nobody has confirmed, undone or changed it since
    /// (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md). Shows the header's Undo; there is no time limit here, unlike the
    /// queue's 30-day "Recently auto-classified" list, because the previous type is stored on the series row.
    /// </summary>
    [ObservableProperty]
    private bool _canUndoContentType;

    /// <summary>"Auto-classified from MangaBaka / AniList: was Unknown".</summary>
    [ObservableProperty]
    private string _undoContentTypeLabel = string.Empty;

    private void RefreshContentTypeUndo(Series series)
    {
        CanUndoContentType = series.ContentTypeSource == ContentTypeSource.Provider && !series.ContentTypeLocked && series.PreviousContentType is not null;
        UndoContentTypeLabel = CanUndoContentType ? $"Auto-classified: was {series.PreviousContentType}" : string.Empty;
    }

    /// <summary>Puts back the type and reading mode from before the automatic classification, locks the series, and reloads the screen.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void UndoContentType()
    {
        if (_seriesId is not int seriesId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = context.Series.Find(seriesId);
            if (series is null || !SeriesContentTypeEditor.Undo(series))
            {
                return;
            }

            context.SaveChanges();
        }

        // Reload on the next dispatcher tick: this runs from the header's own Button, and the reload replaces the screen it sits on.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _goDetailForSeries(seriesId));
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

    /// <summary>True when this series is a mirror of another library's; editing and cover changes are unavailable.</summary>
    public bool IsRemoteSeries { get; private set; }

    /// <summary>The chapters disagree on something and nobody has confirmed the series' values yet; shows a "Needs review" chip that opens Edit Series.</summary>
    [ObservableProperty]
    private bool _needsReview;

    /// <summary>Edit Series is rooted in the series, not in a chapter (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md D11), so it works for a series with no chapters too.</summary>
    public bool CanEdit => !IsRemoteSeries && _seriesId is not null && _editSeries is not null;

    [ObservableProperty]
    private ChapterListFilter _chapterFilter = ChapterListFilter.All;

    /// <summary>The chapter sort - any Library field (detail layouts v2). The "By Number" / "By Date" pills are shortcuts for Number and Released.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChapterSortByNumber), nameof(IsChapterSortByDate))]
    private IssueListSortField _chapterSortBy = IssueListSortField.Number;

    public bool IsChapterSortByNumber => ChapterSortBy == IssueListSortField.Number;

    public bool IsChapterSortByDate => ChapterSortBy == IssueListSortField.Released;

    [ObservableProperty]
    private SortDirection _chapterSortDirection = SortDirection.Ascending;

    /// <summary>Sort-direction disclosure as a real FluentIcons glyph, not a text arrow
    /// (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-design.md §8).</summary>
    public Symbol ChapterSortDirectionSymbol =>
        ChapterSortDirection == SortDirection.Ascending ? Symbol.ArrowSortUp : Symbol.ArrowSortDown;

    public bool HasChapters => Chapters.Count > 0;

    partial void OnChapterFilterChanged(ChapterListFilter value) => RenderChapters();

    partial void OnChapterSortByChanged(IssueListSortField value) => OnChapterSortChanged();

    partial void OnChapterSortDirectionChanged(SortDirection value)
    {
        OnPropertyChanged(nameof(ChapterSortDirectionSymbol));
        OnChapterSortChanged();
    }

    private void OnChapterSortChanged()
    {
        if (_applyingLayout)
        {
            return;
        }

        _detailLayoutState = _detailLayoutState with
        {
            ChapterSortBy = ChapterSortBy,
            ChapterSortDirection = ChapterSortDirection,
            ChapterSortVirtualTagId = ChapterSortBy == IssueListSortField.VirtualTag ? _detailLayoutState.ChapterSortVirtualTagId : null,
        };
        RenderChapters();
        ListLayouts?.TrackChange();
    }

    [RelayCommand]
    private void SetChapterFilter(ChapterListFilter filter) => ChapterFilter = filter;

    [RelayCommand]
    private void SetChapterSortField(ChapterSortField field) =>
        ChapterSortBy = field == ChapterSortField.Date ? IssueListSortField.Released : IssueListSortField.Number;

    [RelayCommand]
    private void ToggleChapterSortDirection() =>
        ChapterSortDirection = ChapterSortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;

    /// <summary>Loads the series with the given id and refreshes every bound field - same shape as <see cref="DetailScreenViewModel.LoadSeries"/>.</summary>
    public void LoadSeries(int seriesId)
    {
        using var context = PaperbunkrDb.CreateContext(includeRemote: true);
        var series = context.Series.Include(s => s.Issues).ThenInclude(i => i.MetadataProposals)
            .Include(s => s.Issues).ThenInclude(i => i.Tags)
            .Include(s => s.MetadataProposals)
            .Include(s => s.Titles)
            .Include(s => s.Credits)
            .AsSplitQuery() // one query per include: joined, each chapter's proposals multiply by its tags (0.3-3.2 s measured)
            .FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return;
        }

        // A series mirrored from another library is read-only here (remote-library-sharing design 7.2): hero Edit / Change Cover go away.
        IsRemoteSeries = series.RemoteSourceId is not null;

        // Until the user confirms the series' values in Edit Series, chapters that disagree (two publishers, two writers) are flagged rather than silently decided
        // (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md D7). In memory only: the chapters are already loaded.
        NeedsReview = !IsRemoteSeries && series.CanonicalReviewVersion == 0
            && Paperbunkr.Data.Metadata.Manga.SeriesCanonicalSeeder.Seed(series, series.Issues).Candidates.Count > 0;

        _isLoadingSeries = true;
        _seriesId = seriesId;
        OnPropertyChanged(nameof(IDetailHeaderSource.SharedElementKey));

        var card = SeriesCardSample.FromSeries(series);
        _coverIssueId = card.CoverIssueId;
        CoverBrush = card.CoverBrush;
        CoverImage = card.CoverKey is string coverKey ? CoverImageCache.Get(coverKey) : null;
        BackdropImage = CoverImage is not null ? BackdropBlurRenderer.Render(CoverImage, new PixelSize(1600, 680)) : null;
        SeriesTitle = series.Name;
        SecondaryTitle = series.Titles
            .Where(t => t.Type is SeriesTitleType.Native or SeriesTitleType.Romanized)
            .Select(t => t.Value)
            .FirstOrDefault(v => !string.Equals(v, series.Name, StringComparison.OrdinalIgnoreCase));
        SelectedContentType = series.ContentType;
        RefreshContentTypeUndo(series);
        StatusLabel = series.Status switch
        {
            SeriesStatus.Ongoing => "Ongoing",
            SeriesStatus.Completed => "Completed",
            SeriesStatus.Cancelled => "Cancelled",
            SeriesStatus.Hiatus => "Hiatus",
            _ => "Unknown",
        };
        StatusAttribution = AttributionLabel(series, MetadataProposalField.Status);
        _readingStatus = series.ReadingStatus == ReadingStatus.Unknown ? null : series.ReadingStatus.ToString();
        OnPropertyChanged(nameof(IDetailHeaderSource.ReadingStatus));
        _readingStatusPicker = new ReadingStatusPickerViewModel(seriesId, onChanged: OnReadingStatusPicked);
        OnPropertyChanged(nameof(IDetailHeaderSource.ReadingStatusPicker));
        Band.ReadingStatusPicker = _readingStatusPicker;
        ReadingModeBadge = ReadingModeCatalog.BadgeFor(series.ReadingMode);
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

        var enabledVirtualTags = context.VirtualTagDefinitions.Where(t => t.IsEnabled).OrderBy(t => t.SortOrder).ToList();
        _virtualTags = enabledVirtualTags;
        _allChapters = series.Issues.OrderByNumber().Select(issue => ToChapterRow(issue, series, enabledVirtualTags)).ToList();
        ChapterFilter = ChapterListFilter.All;

        // A series with its own detail layout shows it; every other series shows the manga default.
        bool ownLayout = ListLayouts is not null && ListLayouts.HasAssignment(ListLayoutsViewModel.SeriesKey(seriesId));
        if (ListLayouts is not null && (ownLayout || _seriesHasOwnLayout))
        {
            ReapplyLayoutForSeries();
        }
        else
        {
            RenderChapters();
        }
        MangaActiveTab = "chapters";

        ChapterCountBadge = _allChapters.Count == 1 ? "1 chapter" : $"{_allChapters.Count} chapters";
        int volumeCount = series.Issues.Select(i => i.EffectiveVolume()).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Count();
        VolumeCountBadge = volumeCount switch
        {
            0 => string.Empty,
            1 => "1 volume",
            _ => $"{volumeCount} volumes",
        };

        Tabs.LoadSeries(series);
        Band.LoadSeries(series, enabledVirtualTags);
        Band.StatusText = StatusLabel;
        Band.PublisherText = string.IsNullOrWhiteSpace(series.Publisher) ? string.Empty : series.Publisher!;
        Band.Summary = Summary;
        Band.IsSynopsisExpanded = false;

        var markIssue = series.Issues.FirstOrDefault(i => i.Id == _coverIssueId) ?? series.Issues.FirstOrDefault();
        Band.FormatText = markIssue?.Format ?? string.Empty;
        Band.AgeRatingText = markIssue?.AgeRating ?? string.Empty;
        Band.SetSpecialMarks(markIssue, SelectedContentTypeText);

        // Series-aggregate fields (Part 4) - publisher/format/rating/language pulled across every
        // chapter, not off the cover one.
        var f = SeriesMetaFields.FromSeries(series);
        _metaBadges = new DetailMetaBadgeGroup(DetailMetaBadge.Build(f.Publisher, StatusLabel,
            series.Status == SeriesStatus.Completed, f.Year, f.Format, f.AgeRating, f.LanguageIso,
            statusKind: series.Status.ToString()));

        // Plain-text line (Part 4 revision, user direction) - separate row under the badges, above
        // the action buttons, same wording the original MetaLine used.
        int unreadChapters = _allChapters.Count(c => !c.IsRead);
        string? unreadBadge = unreadChapters > 0 ? $"{unreadChapters} unread" : null;
        _issueSummaryLine = unreadBadge is null ? ChapterCountBadge : $"{ChapterCountBadge}  ·  {unreadBadge}";
        OnPropertyChanged(nameof(IDetailHeaderSource.IssueSummaryLine));
        OnPropertyChanged(nameof(IDetailHeaderSource.MetaBadges));
        OnPropertyChanged(nameof(IDetailHeaderSource.HasMetaBadges));

        MetaLine = string.Join("  ·  ", new[]
        {
            StatusLabel,
            SourceLabel is "Unknown" ? string.Empty : SourceLabel,
            ReadingModeBadge,
            ChapterCountBadge,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        _isLoadingSeries = false;
        OnPropertyChanged(nameof(CanEdit));
        EditSeriesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Actions));
    }

    private static ChapterRowSample ToChapterRow(Issue issue, Series series, IReadOnlyList<VirtualTagDefinition> virtualTags)
    {
        var date = issue.ReleasedTime ?? issue.AddedTime;
        bool read = issue.HasBeenRead();
        return new ChapterRowSample
        {
            Row = IssueListRow.FromIssue(issue, series, isSelected: null, virtualTags),
            Id = issue.Id,
            DisplayNumber = string.IsNullOrWhiteSpace(issue.EffectiveNumber()) ? "#?" : $"#{issue.EffectiveNumber()}",
            NumberSortKey = issue.NumberSortKey(),
            Title = issue.EffectiveTitle(),
            Volume = issue.EffectiveVolume(),
            IsRead = read,
            IsNew = !read && date >= DateTime.Now.AddDays(-14),
            IsInProgress = issue.IsInProgress(),
            ReadPercentage = issue.ReadPercentage(),
            BookmarkCount = issue.Bookmarks.Count,
            IsMissing = issue.FileIsMissing,
            ScanInformation = issue.ScanInformation,
            Date = date,
        };
    }

    private void RenderChapters()
    {
        IEnumerable<ChapterRowSample> filtered = ChapterFilter switch
        {
            ChapterListFilter.Unread => _allChapters.Where(c => !c.IsRead),
            ChapterListFilter.Bookmarked => _allChapters.Where(c => c.HasBookmark),
            ChapterListFilter.Missing => _allChapters.Where(c => c.IsMissing),
            _ => _allChapters,
        };

        var list = filtered.ToList();
        if (list.All(c => c.Row is not null))
        {
            var comparer = DetailLayoutRendering.Comparer(ChapterSortBy, _detailLayoutState.ChapterSortVirtualTagId, ChapterSortDirection, _virtualTags);
            list.Sort((a, b) => comparer(a.Row!, b.Row!));
        }
        else
        {
            list = (ChapterSortDirection == SortDirection.Ascending ? list.OrderBy(c => c.NumberSortKey) : list.OrderByDescending(c => c.NumberSortKey)).ToList();
        }

        Chapters.Clear();
        foreach (var row in list)
        {
            ConfigureChapterCells(row);
            Chapters.Add(row);
        }

        ChapterHeaderCells.Clear();
        foreach (var cell in DetailLayoutRendering.HeaderCells(_detailLayoutState.ChapterColumns ?? DetailListLayoutState.MangaDefault.ChapterColumns!, manga: true))
        {
            ChapterHeaderCells.Add(cell);
        }

        RebuildChapterGroups();
        OnPropertyChanged(nameof(HasChapters));
    }

    private void ConfigureChapterCells(ChapterRowSample row)
    {
        row.ListCells.Clear();
        var columns = _detailLayoutState.ChapterColumns ?? DetailListLayoutState.MangaDefault.ChapterColumns!;
        row.ShowProgressColumn = columns.Any(c => c.Field == DetailColumnKeys.Progress && c.Visible);
        foreach (var column in columns)
        {
            if (!column.Visible) continue;
            if (DetailLayoutRendering.CatalogCell(column, row.Row, manga: true) is { } catalogCell)
            {
                row.ListCells.Add(catalogCell);
                continue;
            }

            string text = column.Field switch
            {
                DetailColumnKeys.NewStatus => row.IsNew ? "NEW" : string.Empty,
                DetailColumnKeys.ReadStatus => row.ShowReadGlyph ? "Read" : row.IsInProgress ? $"{row.ReadPercentage:0}%" : string.Empty,
                DetailColumnKeys.Bookmark => row.HasBookmark ? "★" : string.Empty,
                DetailColumnKeys.Missing => row.IsMissing ? "Missing" : string.Empty,
                _ => row.IsInProgress ? $"{row.ReadPercentage:0}%" : string.Empty,
            };
            var kind = DetailColumnKeys.IsStatus(column.Field) ? DetailListFieldKind.Status : DetailListFieldKind.Text;
            row.ListCells.Add(new DetailListCell(column.Field, text, column.Width, kind, DetailColumnKeys.DisplayName(column.Field, manga: true)));
        }
    }

    /// <summary>
    /// Groups the currently-visible <see cref="Chapters"/>, preserving <see cref="RenderChapters"/>' sort within each group: by volume
    /// (the default, "No volume" last), not at all, or by a Library group field.
    /// </summary>
    private void RebuildChapterGroups()
    {
        ChapterGroups.Clear();
        string groupBy = _detailLayoutState.ChapterGroupBy;
        if (groupBy == DetailGrouping.None)
        {
            if (Chapters.Count > 0)
            {
                ChapterGroups.Add(new ChapterVolumeGroup { VolumeLabel = "All chapters", Chapters = Chapters.ToList() });
            }

            return;
        }

        if (DetailLayoutRendering.Grouping(groupBy, _virtualTags) is { } grouping && Chapters.All(c => c.Row is not null))
        {
            foreach (var bucket in Chapters.GroupBy(c => grouping.GroupKey(c.Row!)).OrderBy(g => g.Key, Comparer<string>.Create(grouping.GroupOrder)))
            {
                ChapterGroups.Add(new ChapterVolumeGroup { VolumeLabel = bucket.Key, Chapters = bucket.ToList() });
            }

            return;
        }

        var ordered = Chapters
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Volume) ? null : c.Volume!.Trim())
            .OrderBy(g => g.Key is null)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in ordered)
        {
            ChapterGroups.Add(new ChapterVolumeGroup
            {
                VolumeLabel = group.Key is null ? "No volume" : $"Volume {group.Key}",
                Chapters = group.ToList(),
            });
        }
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

    /// <summary>
    /// Header action-row Edit Series button: opens the series editor (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md D11). It replaced the old "Edit",
    /// which opened the properties of whichever chapter Continue would open and so edited one chapter while looking like an edit of the series. Continue is unaffected.
    /// A specific chapter is edited from its row's context menu (<see cref="EditChapterProperties"/>).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void EditSeries()
    {
        if (_seriesId is int seriesId)
        {
            _editSeries?.Invoke(seriesId);
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
            RevealInExplorerHelper.RevealIssue(issue, ActivityCenter);
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

        using var context = PaperbunkrDb.CreateContext(includeRemote: true);
        var issue = context.Issues.Find(row.Id);
        if (issue is null)
        {
            return;
        }

        apply(issue);
        context.SaveChanges();
        ReloadCurrentSeries();

        // "Update progress when marked as read" - mark-as-read only (design §3.3).
        if (apply == IssueReadStateResolver.MarkAsRead)
        {
            _ = _trackerAutoSync.OnIssuesMarkedReadAsync(new[] { issue.SeriesId });
        }
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
    private async Task ChangeSeriesCoverAsync()
    {
        if (_seriesId is not int seriesId) return;
        string? path = await new FilePickerService().PickImageFileAsync("Choose Series Cover Image");
        if (path is not null && new SeriesCoverService().TrySetCustomCover(seriesId, path)) ReloadCurrentSeries();
    }

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

    /// <summary>Queues every chapter of the series that is marked as owing a write to its file; the queue applies the user's write-back settings.</summary>
    private void EnqueueDirtyChapters(int seriesId)
    {
        if (_enqueueMetadataWriteBack is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        foreach (int issueId in context.Issues.AsNoTracking().Where(i => i.SeriesId == seriesId && i.EmbeddedDirty).Select(i => i.Id).ToList())
        {
            _enqueueMetadataWriteBack(issueId);
        }
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
