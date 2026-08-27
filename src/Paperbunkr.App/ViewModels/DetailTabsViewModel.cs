using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Tracking;
using Paperbunkr.Data.Tracking.Adapters;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Issues/Related/Details/Activity tab strip, ported from DetailTabs.dc.html (Claude Design
/// project 43c40b25). Populated from the real <see cref="Series"/> passed to
/// <see cref="LoadSeries"/> rather than the wireframe's static sample data.
/// </summary>
public partial class DetailTabsViewModel : ViewModelBase
{
    private readonly Action<int> _goToProperties;
    private readonly Action<IReadOnlyList<int>> _goToBulkProperties;
    private readonly Action? _onSelectionChanged;
    private readonly Action<int> _onQuickRate;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly IMetadataProvider _metadataProvider;
    private int? _seriesId;
    private readonly TileSelectionController<IssueCardSample> _selection = new();

    public DetailTabsViewModel(Action<int> goToProperties, Action<IReadOnlyList<int>> goToBulkProperties, Action? onSelectionChanged = null, Action<int>? onQuickRate = null)
        : this(goToProperties, goToBulkProperties, onSelectionChanged, PaperbunkrDb.CreateContext, new AniListMetadataProvider(AniListHttpClient.Shared), onQuickRate)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database and a real <see cref="AniListMetadataProvider"/>).</summary>
    internal DetailTabsViewModel(Action<int> goToProperties, Action<IReadOnlyList<int>> goToBulkProperties, Action? onSelectionChanged, Func<PaperbunkrDbContext> contextFactory, IMetadataProvider? metadataProvider = null, Action<int>? onQuickRate = null)
    {
        _goToProperties = goToProperties;
        _goToBulkProperties = goToBulkProperties;
        _onSelectionChanged = onSelectionChanged;
        _onQuickRate = onQuickRate ?? (_ => { });
        _contextFactory = contextFactory;
        _metadataProvider = metadataProvider ?? new AniListMetadataProvider(AniListHttpClient.Shared);
        Issues = new ObservableCollection<IssueCardSample>();
        Related = new ObservableCollection<RelatedSeriesSample>();
        SameContinuity = new ObservableCollection<RelatedGroupSeriesSample>();
        ContinuityChips = new ObservableCollection<ContinuityChip>();
        SameEvent = new ObservableCollection<RelatedGroupSeriesSample>();
        ExternalLinks = new ObservableCollection<ExternalLinkSample>();
        MetadataSearchResults = new ObservableCollection<AniListMatchSample>();
    }

    /// <summary>
    /// Multi-select state for the Issues tab (docs/superpowers/specs/2026-08-07-bulk-issue-editing-
    /// design.md §1) - backed by the shared <see cref="TileSelectionController{TCard}"/> (docs/
    /// superpowers/specs/2026-08-24-library-multiselect-slice1-design.md §2) as of this app's second
    /// screen needing the same selection shape (Library's issue grids).
    /// </summary>
    public IReadOnlySet<int> SelectedIssueIds => _selection.SelectedIds;

    /// <summary>
    /// Set to <see langword="false"/> when this instance is embedded in <c>MangaDetailScreenViewModel</c>
    /// (docs/superpowers/specs/2026-08-23-manga-detail-screen-design.md) - that screen has its own
    /// Chapters tab/list replacing this one's Issues tab, so the Issues tab button/content here would
    /// be a confusing duplicate. Left <see langword="true"/> (default) for the Western
    /// <see cref="DetailScreenViewModel"/>, which still relies on this tab. Set once before the first
    /// <see cref="LoadSeries"/> call, not observed for further changes.
    /// </summary>
    public bool ShowIssuesTab { get; set; } = true;

    /// <summary>
    /// Also set to <see langword="false"/> by <c>MangaDetailScreenViewModel</c>: that screen builds
    /// its own unified 4-button tab strip (Chapters/Related/Details/Activity) rather than showing
    /// this view's own tab-strip row underneath it, which would otherwise duplicate it. Content
    /// visibility (<see cref="IsRelatedTab"/>/<see cref="IsDetailsTab"/>/<see cref="IsActivityTab"/>)
    /// is unaffected - the manga screen drives <see cref="ActiveTab"/> via this instance's own
    /// <see cref="GoRelatedCommand"/>/<see cref="GoDetailsCommand"/>/<see cref="GoActivityCommand"/>,
    /// same as this view's own strip would.
    /// </summary>
    public bool ShowTabStrip { get; set; } = true;

    public ObservableCollection<IssueCardSample> Issues { get; }

    /// <summary>
    /// Real data as of docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-relations-
    /// design.md - populated by <see cref="LoadSeries"/> via <see cref="MediaRelationResolver.GetRelatedSeries"/>.
    /// </summary>
    public ObservableCollection<RelatedSeriesSample> Related { get; }

    public bool HasRelated => Related.Count > 0;

    /// <summary>
    /// Real data as of docs/superpowers/specs/2026-08-17-metadata-model-phase4a-continuity-
    /// design.md - other series sharing at least one <see cref="Data.Entities.Continuity"/> with
    /// the loaded series, populated by <see cref="LoadSeries"/> via
    /// <see cref="ContinuityResolver.GetOtherSeriesSharingContinuity"/>. Additive alongside
    /// <see cref="Related"/>, not a replacement for it - Phase 3's flat relation carousel is
    /// unchanged.
    /// </summary>
    public ObservableCollection<RelatedGroupSeriesSample> SameContinuity { get; }

    public bool HasSameContinuity => SameContinuity.Count > 0;

    /// <summary>The loaded series' own <see cref="Data.Entities.Continuity"/> memberships, shown as removable chips.</summary>
    public ObservableCollection<ContinuityChip> ContinuityChips { get; }

    /// <summary>
    /// Real data as of docs/superpowers/specs/2026-08-17-metadata-model-phase4b-story-events-
    /// design.md - other series sharing at least one <see cref="Data.Entities.StoryEvent"/> with
    /// the loaded series, populated by <see cref="LoadSeries"/> via
    /// <see cref="EventMembershipResolver.GetOtherSeriesInSharedEvents"/>. Additive alongside
    /// <see cref="Related"/>/<see cref="SameContinuity"/>.
    /// </summary>
    public ObservableCollection<RelatedGroupSeriesSample> SameEvent { get; }

    public bool HasSameEvent => SameEvent.Count > 0;

    public ObservableCollection<SeriesSearchResult> RelationSearchResults { get; } = new();

    public string Publisher { get; private set; } = "Unknown";
    public string ReadingModeLabel { get; private set; } = "Left to Right";

    public void LoadSeries(Series series)
    {
        var coverBrush = SeriesCardSample.CoverBrushFor(series.Name);

        Issues.Clear();
        _selection.Clear();
        foreach (var issue in series.Issues.OrderByNumber())
        {
            Issues.Add(new IssueCardSample
            {
                Id = issue.Id,
                SeriesId = series.Id,
                Title = string.IsNullOrWhiteSpace(issue.EffectiveNumber()) ? "#?" : $"#{issue.EffectiveNumber()}",
                IsUnread = issue.LastPageRead is null or 0,
                CoverBrush = coverBrush,
                CoverImage = CoverImageCache.Get(issue.Id, issue.FilePath, issue.FileSize),
                FilePath = issue.FilePath,
            });
        }

        _seriesId = series.Id;
        Publisher = string.IsNullOrWhiteSpace(series.Publisher) ? "Unknown" : series.Publisher;
        SetReadingModeLabel(series.ReadingMode);
        OnPropertyChanged(nameof(Publisher));

        using (var context = _contextFactory())
        {
            RefreshRelated(context, series.Id);
            RefreshContinuity(context, series.Id);
            RefreshSameEvent(context, series.Id);
            RefreshExternalLinks(context, series.Id);
            RefreshTrackerLinks(context, series.Id);
        }

        IsSearchingMetadata = false;
        MetadataSearchQuery = string.Empty;
        MetadataSearchResults.Clear();

        IsLinkingTracker = false;
        TrackerSearchQuery = string.Empty;
        TrackerSearchResults.Clear();
        TrackerSyncStatus = null;

        ActiveTab = ShowIssuesTab ? "issues" : "related";
        _onSelectionChanged?.Invoke();
    }

    private void RefreshRelated(PaperbunkrDbContext context, int seriesId)
    {
        Related.Clear();
        foreach (var (otherSeries, displayType, mediaRelationId) in MediaRelationResolver.GetRelatedSeries(context, seriesId))
        {
            Related.Add(new RelatedSeriesSample
            {
                Title = otherSeries.Name,
                Name = otherSeries.Name,
                Note = RelationTypeOption.FormatLabel(displayType),
                CoverBrush = SeriesCardSample.CoverBrushFor(otherSeries.Name),
                RelatedSeriesId = otherSeries.Id,
                MediaRelationId = mediaRelationId,
            });
        }

        OnPropertyChanged(nameof(HasRelated));
    }

    // --- Related tab: add/remove a MediaRelation (docs/superpowers/specs/2026-08-17-metadata-
    // model-phase3-media-relations-design.md) ---

    public static IReadOnlyList<RelationTypeOption> RelationTypeOptions => RelationTypeOption.All;

    [ObservableProperty]
    private bool _isAddingRelation;

    [ObservableProperty]
    private string _relationSearchQuery = string.Empty;

    [ObservableProperty]
    private RelationType _selectedRelationType = RelationType.Related;

    /// <summary>
    /// Bound to the ComboBox's <c>SelectedItem</c> instead of <c>SelectedValue</c>/
    /// <c>SelectedValueBinding</c> - the latter resolves its binding path against this
    /// ViewModel's own DataContext, not the <c>ItemsSource</c> element type, so `{Binding Type}`
    /// there was silently unresolvable (a real, permanent XAML bug since Phase 3, not a build-
    /// tooling artifact - see docs/superpowers/specs/2026-08-18-selectedvaluebinding-xaml-fix-
    /// design.md).
    /// </summary>
    [ObservableProperty]
    private RelationTypeOption _selectedRelationTypeOption = RelationTypeOptions.First(o => o.Type == RelationType.Related);

    partial void OnSelectedRelationTypeOptionChanged(RelationTypeOption value) => SelectedRelationType = value.Type;

    [RelayCommand]
    private void ToggleAddRelation()
    {
        IsAddingRelation = !IsAddingRelation;
        RelationSearchQuery = string.Empty;
        RelationSearchResults.Clear();
    }

    partial void OnRelationSearchQueryChanged(string value) => SearchRelationCandidates();

    [RelayCommand]
    private void SearchRelationCandidates()
    {
        RelationSearchResults.Clear();
        if (string.IsNullOrWhiteSpace(RelationSearchQuery) || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        var matches = context.Series
            .Where(s => s.Id != currentSeriesId)
            .AsEnumerable()
            .Where(s => s.Name.Contains(RelationSearchQuery, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var series in matches)
        {
            RelationSearchResults.Add(new SeriesSearchResult { SeriesId = series.Id, Name = series.Name });
        }
    }

    [RelayCommand]
    private void AddRelation(SeriesSearchResult? target)
    {
        if (target is null || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        MediaRelationResolver.TryCreate(context, currentSeriesId, target.SeriesId, SelectedRelationType);

        IsAddingRelation = false;
        RelationSearchQuery = string.Empty;
        RelationSearchResults.Clear();
        RefreshRelated(context, currentSeriesId);
    }

    [RelayCommand]
    private void RemoveRelation(RelatedSeriesSample? sample)
    {
        if (sample is null || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        MediaRelationResolver.Remove(context, sample.MediaRelationId);
        RefreshRelated(context, currentSeriesId);
    }

    // --- Related tab: Continuity membership (docs/superpowers/specs/2026-08-17-metadata-model-
    // phase4a-continuity-design.md) ---

    private void RefreshContinuity(PaperbunkrDbContext context, int seriesId)
    {
        ContinuityChips.Clear();
        foreach (var continuity in ContinuityResolver.GetContinuities(context, seriesId))
        {
            ContinuityChips.Add(new ContinuityChip { ContinuityId = continuity.Id, Name = continuity.Name });
        }

        SameContinuity.Clear();
        foreach (var otherSeries in ContinuityResolver.GetOtherSeriesSharingContinuity(context, seriesId))
        {
            SameContinuity.Add(new RelatedGroupSeriesSample
            {
                Title = otherSeries.Name,
                Name = otherSeries.Name,
                Note = "Same continuity",
                CoverBrush = SeriesCardSample.CoverBrushFor(otherSeries.Name),
                SeriesId = otherSeries.Id,
            });
        }

        OnPropertyChanged(nameof(HasSameContinuity));
    }

    [ObservableProperty]
    private bool _isAddingContinuity;

    [ObservableProperty]
    private string _continuitySearchQuery = string.Empty;

    public ObservableCollection<ContinuitySearchResult> ContinuitySearchResults { get; } = new();

    [RelayCommand]
    private void ToggleAddContinuity()
    {
        IsAddingContinuity = !IsAddingContinuity;
        ContinuitySearchQuery = string.Empty;
        ContinuitySearchResults.Clear();
    }

    partial void OnContinuitySearchQueryChanged(string value) => SearchContinuityCandidates();

    [RelayCommand]
    private void SearchContinuityCandidates()
    {
        ContinuitySearchResults.Clear();
        string query = ContinuitySearchQuery.Trim();
        if (query.Length == 0)
        {
            return;
        }

        using var context = _contextFactory();
        var matches = context.Continuities
            .AsEnumerable()
            .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();

        foreach (var continuity in matches)
        {
            ContinuitySearchResults.Add(new ContinuitySearchResult { ContinuityId = continuity.Id, Name = continuity.Name });
        }

        // No exact (case-insensitive) match among the results - offer a "create new" row rather
        // than requiring a separate action, matching ContinuityResolver.GetOrCreate's own
        // case-insensitive matching so this picker can never produce a near-duplicate.
        if (!matches.Any(c => string.Equals(c.Name, query, StringComparison.OrdinalIgnoreCase)))
        {
            ContinuitySearchResults.Add(new ContinuitySearchResult { Name = query, IsNew = true });
        }
    }

    [RelayCommand]
    private void AddContinuity(ContinuitySearchResult? target)
    {
        if (target is null || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        var continuity = target.IsNew ? ContinuityResolver.GetOrCreate(context, target.Name) : context.Continuities.Find(target.ContinuityId);
        if (continuity is not null)
        {
            ContinuityResolver.AddSeriesToContinuity(context, currentSeriesId, continuity.Id);
        }

        IsAddingContinuity = false;
        ContinuitySearchQuery = string.Empty;
        ContinuitySearchResults.Clear();
        RefreshContinuity(context, currentSeriesId);
    }

    [RelayCommand]
    private void RemoveContinuity(ContinuityChip? chip)
    {
        if (chip is null || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        ContinuityResolver.RemoveSeriesFromContinuity(context, currentSeriesId, chip.ContinuityId);
        RefreshContinuity(context, currentSeriesId);
    }

    // --- Related tab: Same Event (docs/superpowers/specs/2026-08-17-metadata-model-phase4b-
    // story-events-design.md) - read-only, populated from EventMembership, no creation UI here
    // (that lives on the Events screen). ---

    private void RefreshSameEvent(PaperbunkrDbContext context, int seriesId)
    {
        SameEvent.Clear();
        foreach (var otherSeries in EventMembershipResolver.GetOtherSeriesInSharedEvents(context, seriesId))
        {
            SameEvent.Add(new RelatedGroupSeriesSample
            {
                Title = otherSeries.Name,
                Name = otherSeries.Name,
                Note = "Same event",
                CoverBrush = SeriesCardSample.CoverBrushFor(otherSeries.Name),
                SeriesId = otherSeries.Id,
            });
        }

        OnPropertyChanged(nameof(HasSameEvent));
    }

    // --- Details tab: external metadata provider links (docs/superpowers/specs/2026-08-19-
    // metadata-model-anilist-search-and-link-design.md) - completes the search/match UI Phase 5b's
    // own spec explicitly deferred ("nothing in Paperbunkr.App calls this yet"). ---

    public ObservableCollection<ExternalLinkSample> ExternalLinks { get; }

    public bool HasExternalLinks => ExternalLinks.Count > 0;

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

    /// <summary>
    /// Second provider alongside AniList - MangaBaka is search/link only (no auth, no per-user
    /// library, confirmed against its live API), same shape as AniList's own metadata half.
    /// </summary>
    public static readonly ExternalMetadataProvider[] MetadataProviderOptions =
    {
        ExternalMetadataProvider.AniList, ExternalMetadataProvider.MangaBaka,
    };

    [ObservableProperty]
    private ExternalMetadataProvider _selectedMetadataProvider = ExternalMetadataProvider.AniList;

    /// <summary>Fresh instance per call for MangaBaka, same "no DI container" rationale as
    /// <see cref="GetTrackerSearchProvider"/> - AniList keeps using the injected/test-seam
    /// <see cref="_metadataProvider"/> instance instead of constructing a second one.</summary>
    private IMetadataProvider GetMetadataProviderFor(ExternalMetadataProvider provider) => provider switch
    {
        ExternalMetadataProvider.MangaBaka => MangaBakaMetadataProvider.Shared,
        _ => _metadataProvider,
    };

    [ObservableProperty]
    private bool _isSearchingMetadata;

    [ObservableProperty]
    private string _metadataSearchQuery = string.Empty;

    [ObservableProperty]
    private bool _isMetadataSearchInProgress;

    [ObservableProperty]
    private string? _metadataSearchError;

    public ObservableCollection<AniListMatchSample> MetadataSearchResults { get; }

    /// <summary>Cancels a still-running search if the user reopens/retriggers it before the prior one completes - a slow provider response must not race a newer query's results into the list.</summary>
    private CancellationTokenSource? _searchCts;

    [RelayCommand]
    private void ToggleSearchMetadata()
    {
        IsSearchingMetadata = !IsSearchingMetadata;
        MetadataSearchQuery = string.Empty;
        MetadataSearchError = null;
        MetadataSearchResults.Clear();
    }

    [RelayCommand]
    private async Task SearchMetadataAsync()
    {
        _searchCts?.Cancel();
        MetadataSearchResults.Clear();
        MetadataSearchError = null;

        if (string.IsNullOrWhiteSpace(MetadataSearchQuery) || _seriesId is not int currentSeriesId)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        IsMetadataSearchInProgress = true;
        try
        {
            using var context = _contextFactory();
            var provider = GetMetadataProviderFor(SelectedMetadataProvider);
            var matches = await MetadataLinkResolver.SearchAsync(provider, context, currentSeriesId, MetadataSearchQuery, cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            foreach (var match in matches)
            {
                MetadataSearchResults.Add(new AniListMatchSample
                {
                    ExternalId = match.Result.ExternalId,
                    Title = match.Result.Title,
                    Url = match.Result.Url,
                    Confidence = match.Confidence,
                    Tier = match.Tier,
                });
            }

            if (matches.Count == 0)
            {
                MetadataSearchError = "No matches found.";
            }
        }
        catch (Exception) when (!cts.IsCancellationRequested)
        {
            // Network/provider failure (docs/superpowers/specs/2026-08-19-metadata-model-anilist-
            // search-and-link-design.md) - the library must keep working when a provider is
            // unavailable, so this surfaces as an inline message, not an unhandled exception.
            MetadataSearchError = $"{SelectedMetadataProvider} is unavailable right now. Try again later.";
        }
        finally
        {
            IsMetadataSearchInProgress = false;
        }
    }

    [RelayCommand]
    private async Task LinkMetadataAsync(AniListMatchSample? candidate)
    {
        if (candidate is null || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        var provider = GetMetadataProviderFor(SelectedMetadataProvider);
        bool linked = await MetadataLinkResolver.LinkAsync(provider, context, currentSeriesId, candidate.ExternalId, CancellationToken.None);
        if (!linked)
        {
            MetadataSearchError = "Couldn't fetch that match's details. Try again later.";
            return;
        }

        RefreshExternalLinks(context, currentSeriesId);
        IsSearchingMetadata = false;
        MetadataSearchQuery = string.Empty;
        MetadataSearchResults.Clear();
    }

    [RelayCommand]
    private void UnlinkMetadata(ExternalLinkSample? link)
    {
        // Removes only the ProviderLink row, per the "removing a metadata provider must not delete
        // the Work" invariant - ExternalMetadataSnapshot rows (the audit trail) and any SeriesTitle
        // rows this link contributed are left untouched.
        if (link is null || _seriesId is not int currentSeriesId || !Enum.TryParse<ExternalMetadataProvider>(link.ProviderLabel, out var provider))
        {
            return;
        }

        using var context = _contextFactory();
        var existing = context.ExternalMediaIds.FirstOrDefault(e => e.SeriesId == currentSeriesId && e.Provider == provider && e.ExternalId == link.ExternalId);
        if (existing is not null)
        {
            context.ExternalMediaIds.Remove(existing);
            context.SaveChanges();
        }

        RefreshExternalLinks(context, currentSeriesId);
    }

    // --- Details tab: tracker linking + sync (docs/superpowers/specs/2026-08-23-tracker-write-
    // back-sync-design.md) - deliberately separate from the metadata linking above: TrackerLinkResolver
    // touches only TrackingLink (never ExternalMediaId/SeriesTitle/ExternalMetadataSnapshot), and
    // linking here carries real account-write consequences the metadata search above does not. ---

    public static readonly TrackingService[] TrackerServiceOptions =
    {
        TrackingService.AniList, TrackingService.MyAnimeList, TrackingService.Shikimori, TrackingService.Bangumi, TrackingService.MangaBaka,
    };

    public ObservableCollection<TrackerLinkSample> TrackerLinks { get; } = new();

    public bool HasTrackerLinks => TrackerLinks.Count > 0;

    /// <summary>At-a-glance tracker indicator for the manga detail header's action row
    /// (docs/superpowers/specs/2026-08-23-manga-detail-screen-design.md) - "1 tracker" per the
    /// Komikku reference screenshot notes that spec cites. Unused by the Western
    /// <see cref="DetailScreenViewModel"/>'s own layout, but harmless to compute unconditionally.</summary>
    public string TrackerStatusLabel => TrackerLinks.Count switch
    {
        0 => "Not tracked",
        1 => "1 tracker",
        int n => $"{n} trackers",
    };

    private void RefreshTrackerLinks(PaperbunkrDbContext context, int seriesId)
    {
        TrackerLinks.Clear();
        foreach (var link in context.TrackingLinks.Where(t => t.SeriesId == seriesId))
        {
            TrackerLinks.Add(new TrackerLinkSample { Service = link.Service, ExternalId = link.ExternalId, Url = null });
        }

        OnPropertyChanged(nameof(HasTrackerLinks));
        OnPropertyChanged(nameof(TrackerStatusLabel));
    }

    [ObservableProperty]
    private bool _isLinkingTracker;

    [ObservableProperty]
    private TrackingService _selectedTrackerService = TrackingService.AniList;

    [ObservableProperty]
    private string _trackerSearchQuery = string.Empty;

    [ObservableProperty]
    private bool _isTrackerSearchInProgress;

    [ObservableProperty]
    private string? _trackerSearchError;

    [ObservableProperty]
    private string? _trackerSyncStatus;

    public ObservableCollection<TrackerMatchSample> TrackerSearchResults { get; } = new();

    private CancellationTokenSource? _trackerSearchCts;

    [RelayCommand]
    private void ToggleLinkTracker()
    {
        IsLinkingTracker = !IsLinkingTracker;
        TrackerSearchQuery = string.Empty;
        TrackerSearchError = null;
        TrackerSearchResults.Clear();
    }

    /// <summary>Every tracker's search is a fresh instance per call - these are stateless HTTP
    /// callers (no DI container in this app, matching every other provider construction site), and
    /// MyAnimeList's needs its Client ID read from <see cref="CredentialStore"/> first since
    /// <see cref="ITrackerSearchProvider.SearchAsync"/>'s shared signature takes no context.</summary>
    private ITrackerSearchProvider? GetTrackerSearchProvider(PaperbunkrDbContext context, TrackingService service) => service switch
    {
        TrackingService.AniList => _metadataProvider as ITrackerSearchProvider,
        TrackingService.MyAnimeList => new MyAnimeListTrackerAdapter(TrackerHttpClients.MyAnimeList, CredentialStore.Get(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthClientId)),
        TrackingService.Shikimori => new ShikimoriTrackerAdapter(TrackerHttpClients.Shikimori),
        TrackingService.Bangumi => new BangumiTrackerAdapter(TrackerHttpClients.Bangumi),
        TrackingService.MangaBaka => MangaBakaMetadataProvider.Shared,
        _ => null,
    };

    private ITrackerAdapter GetTrackerAdapter(TrackingService service) => service switch
    {
        TrackingService.MyAnimeList => new MyAnimeListTrackerAdapter(TrackerHttpClients.MyAnimeList, clientId: null),
        TrackingService.Shikimori => new ShikimoriTrackerAdapter(TrackerHttpClients.Shikimori),
        TrackingService.Bangumi => new BangumiTrackerAdapter(TrackerHttpClients.Bangumi),
        TrackingService.MangaBaka => new MangaBakaTrackerAdapter(MangaBakaHttpClient.Shared),
        _ => new AniListTrackerAdapter(AniListHttpClient.Shared),
    };

    [RelayCommand]
    private async Task SearchTrackerAsync()
    {
        _trackerSearchCts?.Cancel();
        TrackerSearchResults.Clear();
        TrackerSearchError = null;

        if (string.IsNullOrWhiteSpace(TrackerSearchQuery) || _seriesId is not int currentSeriesId)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _trackerSearchCts = cts;
        IsTrackerSearchInProgress = true;
        try
        {
            using var context = _contextFactory();
            var provider = GetTrackerSearchProvider(context, SelectedTrackerService);
            if (provider is null)
            {
                TrackerSearchError = "This tracker isn't available right now.";
                return;
            }

            var matches = await TrackerLinkResolver.SearchAsync(provider, context, currentSeriesId, TrackerSearchQuery, cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            foreach (var match in matches)
            {
                var candidate = match;
                TrackerSearchResults.Add(new TrackerMatchSample
                {
                    ExternalId = candidate.Result.ExternalId,
                    Title = candidate.Result.Title,
                    Url = candidate.Result.Url,
                    Confidence = candidate.Confidence,
                    Tier = candidate.Tier,
                    LinkConfirm = new TwoStepConfirm(
                        () => LinkTracker(candidate.Result.ExternalId),
                        idleLabel: "Link for tracking",
                        armedLabel: "Confirm - writes to your account?"),
                });
            }

            if (matches.Count == 0)
            {
                TrackerSearchError = "No matches found.";
            }
        }
        catch (Exception) when (!cts.IsCancellationRequested)
        {
            TrackerSearchError = "This tracker is unavailable right now. Try again later.";
        }
        finally
        {
            IsTrackerSearchInProgress = false;
        }
    }

    private void LinkTracker(string externalId)
    {
        if (_seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        TrackerLinkResolver.Link(context, currentSeriesId, SelectedTrackerService, externalId);

        RefreshTrackerLinks(context, currentSeriesId);
        IsLinkingTracker = false;
        TrackerSearchQuery = string.Empty;
        TrackerSearchResults.Clear();
    }

    [RelayCommand]
    private void UnlinkTracker(TrackerLinkSample? link)
    {
        if (link is null || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        TrackerLinkResolver.Unlink(context, currentSeriesId, link.Service);
        RefreshTrackerLinks(context, currentSeriesId);
    }

    /// <summary>Pushes this series' <see cref="Series.ReadingStatus"/> + chapter progress to every
    /// currently-connected tracker it's linked to - one action regardless of how many trackers are
    /// linked, per this feature's design.</summary>
    [RelayCommand]
    private async Task SyncToTrackersAsync()
    {
        if (_seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        var series = context.Series.Include(s => s.Issues).Include(s => s.TrackingLinks).FirstOrDefault(s => s.Id == currentSeriesId);
        if (series is null)
        {
            return;
        }

        var payload = new TrackerPushPayload(series.ReadingStatus, TrackerProgressCalculator.ComputeChapterProgress(series.Issues));

        var succeeded = new List<string>();
        var failed = new List<string>();
        foreach (var link in series.TrackingLinks)
        {
            bool isConnected = link.Service switch
            {
                TrackingService.AniList => CredentialStore.HasCredentials(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken),
                TrackingService.MyAnimeList => CredentialStore.HasCredentials(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken),
                TrackingService.Shikimori => CredentialStore.HasCredentials(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken),
                TrackingService.Bangumi => CredentialStore.HasCredentials(context, nameof(TrackingService.Bangumi), CredentialKind.ApiKey),
                TrackingService.MangaBaka => CredentialStore.HasCredentials(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey),
                _ => false,
            };
            if (!isConnected)
            {
                continue;
            }

            bool ok = await GetTrackerAdapter(link.Service).PushEntryAsync(context, link, payload, CancellationToken.None);
            (ok ? succeeded : failed).Add(link.Service.ToString());
        }

        if (succeeded.Count == 0 && failed.Count == 0)
        {
            TrackerSyncStatus = "No connected trackers linked to this series.";
        }
        else
        {
            string summary = succeeded.Count > 0 ? $"Synced to {string.Join(", ", succeeded)}." : string.Empty;
            string failureSummary = failed.Count > 0 ? $" {string.Join(", ", failed)} sync failed, try again later." : string.Empty;
            TrackerSyncStatus = (summary + failureSummary).Trim();
        }
    }

    private void SetReadingModeLabel(ReadingMode mode)
    {
        ReadingModeLabel = mode switch
        {
            ReadingMode.RightToLeft => "Right to Left ▾",
            ReadingMode.TopToBottom => "Vertical ▾",
            ReadingMode.VerticalContinuous => "Vertical (Continuous) ▾",
            ReadingMode.HorizontalContinuous => "Horizontal (Continuous) ▾",
            _ => "Left to Right ▾",
        };
        OnPropertyChanged(nameof(ReadingModeLabel));
    }

    /// <summary>
    /// Binary flip only (docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md §5) -
    /// <see cref="Entities.ReadingMode.VerticalContinuous"/>/<see cref="Entities.ReadingMode.HorizontalContinuous"/>
    /// are unreachable via any UI today (only CE migration data produces them, and there's no
    /// scroll-paging implementation behind them yet), so toggling from either of those collapses
    /// to <see cref="Entities.ReadingMode.RightToLeft"/> rather than growing a full mode picker.
    /// </summary>
    [RelayCommand]
    private void ToggleReadingMode()
    {
        if (_seriesId is not int seriesId)
        {
            return;
        }

        using var context = _contextFactory();
        var series = context.Series.FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return;
        }

        series.ReadingMode = series.ReadingMode == ReadingMode.RightToLeft ? ReadingMode.LeftToRight : ReadingMode.RightToLeft;
        context.SaveChanges();

        SetReadingModeLabel(series.ReadingMode);
    }

    [ObservableProperty]
    private string _activeTab = "issues";

    public bool IsIssuesTab => ActiveTab == "issues";
    public bool IsRelatedTab => ActiveTab == "related";
    public bool IsDetailsTab => ActiveTab == "details";
    public bool IsActivityTab => ActiveTab == "activity";

    partial void OnActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsIssuesTab));
        OnPropertyChanged(nameof(IsRelatedTab));
        OnPropertyChanged(nameof(IsDetailsTab));
        OnPropertyChanged(nameof(IsActivityTab));
    }

    [RelayCommand]
    private void GoIssues() => ActiveTab = "issues";

    [RelayCommand]
    private void GoRelated() => ActiveTab = "related";

    [RelayCommand]
    private void GoDetails() => ActiveTab = "details";

    [RelayCommand]
    private void GoActivity() => ActiveTab = "activity";

    /// <summary>
    /// Left-click tile selection (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md
    /// §1): plain click toggles the clicked tile; shift-click additionally selects the contiguous
    /// range from the last-toggled tile to this one, without clearing the existing selection.
    /// </summary>
    public void ToggleIssueSelection(IssueCardSample issue, bool isShiftHeld)
    {
        _selection.Toggle(Issues, issue, isShiftHeld);
        _onSelectionChanged?.Invoke();
    }

    /// <summary>
    /// Right-click entry point into the Issue Properties editor(s) (docs/superpowers/specs/
    /// 2026-08-07-issue-properties-editor-design.md §2, extended by docs/superpowers/specs/
    /// 2026-08-07-bulk-issue-editing-design.md §2) - operates on the current selection union the
    /// right-clicked tile, so right-clicking a lone unselected tile with nothing else selected
    /// (today's entire pre-existing behavior) still just edits that one issue.
    /// </summary>
    [RelayCommand]
    private void EditIssueProperties(IssueCardSample issue)
    {
        var ids = _selection.UnionForAction(issue.Id);

        if (ids.Count == 1)
        {
            _goToProperties(ids[0]);
        }
        else
        {
            _goToBulkProperties(ids);
        }
    }

    /// <summary>
    /// Right-click "Show in Explorer" (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-
    /// fileless-entries-design.md §1) - same selection-union shape as <see cref="EditIssueProperties"/>
    /// above, so right-clicking a lone unselected tile still just reveals that one file, but a
    /// multi-selection reveals every uniquely-folder'd file at once. No cross-screen navigation
    /// needed (pure OS side effect), so this stays entirely local - no injected callback.
    /// </summary>
    [RelayCommand]
    private void RevealIssue(IssueCardSample issue)
    {
        var ids = _selection.UnionForAction(issue.Id);

        using var context = _contextFactory();
        var issues = context.Issues.Where(i => ids.Contains(i.Id)).ToList();

        if (issues.Count == 1)
        {
            RevealInExplorerHelper.RevealIssue(issues[0]);
        }
        else
        {
            RevealInExplorerHelper.RevealIssues(issues);
        }
    }

    /// <summary>
    /// Right-click "Mark as Read"/"Mark as Unread" (docs/superpowers/specs/2026-08-23-mark-as-read-
    /// design.md) - same selection-union shape as <see cref="EditIssueProperties"/>/
    /// <see cref="RevealIssue"/> above, so a multi-selection marks every selected issue at once
    /// (real bulk support, unlike the Library grid's single-item-only version - this screen already
    /// has the selection model that one lacks).
    /// </summary>
    [RelayCommand]
    private void MarkIssueRead(IssueCardSample issue) => MarkIssuesReadState(issue, IssueReadStateResolver.MarkAsRead);

    [RelayCommand]
    private void MarkIssueUnread(IssueCardSample issue) => MarkIssuesReadState(issue, IssueReadStateResolver.MarkAsUnread);

    /// <summary>Quick Rating + free-text Review in one popup (docs/ce-feature-inventory.md §A) - always the single clicked tile, unlike Mark as Read/Unread above, which deliberately extends to selection ∪ clicked tile; a shared rating/review across several issues at once doesn't make sense the same way a shared read-state does.</summary>
    [RelayCommand]
    private void QuickRate(IssueCardSample issue) => _onQuickRate(issue.Id);

    private void MarkIssuesReadState(IssueCardSample issue, Action<Issue> apply)
    {
        var ids = (SelectedIssueIds.Count > 0 ? SelectedIssueIds.Append(issue.Id) : new[] { issue.Id })
            .Distinct()
            .ToList();

        using var context = _contextFactory();
        var affected = context.Issues.Where(i => ids.Contains(i.Id)).ToList();
        foreach (var i in affected)
        {
            apply(i);
        }

        context.SaveChanges();

        // Same targeted-tile-swap shape as RefreshIssueTile, but recomputing IsUnread (that one
        // preserves the old value verbatim - fine for a cover-only change, not for this) rather
        // than a full LoadSeries, so the rest of the current selection survives the refresh.
        foreach (var updated in affected)
        {
            int index = Issues.ToList().FindIndex(card => card.Id == updated.Id);
            if (index < 0)
            {
                continue;
            }

            var old = Issues[index];
            Issues[index] = new IssueCardSample
            {
                Id = old.Id,
                SeriesId = old.SeriesId,
                Title = old.Title,
                IsUnread = updated.LastPageRead is null or 0,
                CoverBrush = old.CoverBrush,
                CoverImage = old.CoverImage,
                FilePath = old.FilePath,
                IsSelected = old.IsSelected,
            };
        }
    }

    // --- Cover art override (docs/superpowers/specs/2026-08-23-cover-art-override-design.md),
    // Issues-tab tile entry point - same ChangeCover/ResetCover pair as DetailScreenViewModel/
    // MangaDetailScreenViewModel's own header-level actions, targeting this specific tile's issue
    // instead. Swaps just that one tile's IssueCardSample (CoverImage is init-only) rather than a
    // full LoadSeries, so the rest of the current selection survives the refresh. ---

    [RelayCommand]
    private async Task ChangeIssueCoverAsync(IssueCardSample? issue)
    {
        if (issue is null)
        {
            return;
        }

        string? path = await new FilePickerService().PickImageFileAsync("Choose Cover Image");
        if (path is null)
        {
            return;
        }

        if (new CoverThumbnailService().TrySetCustomCover(issue.Id, path))
        {
            RefreshIssueTile(issue.Id);
        }
    }

    [RelayCommand]
    private void ResetIssueCover(IssueCardSample? issue)
    {
        if (issue is null)
        {
            return;
        }

        using var context = _contextFactory();
        string? filePath = context.Issues.Find(issue.Id)?.FilePath;
        new CoverThumbnailService().ResetCover(issue.Id, filePath);
        RefreshIssueTile(issue.Id);
    }

    private void RefreshIssueTile(int issueId)
    {
        int index = Issues.ToList().FindIndex(i => i.Id == issueId);
        if (index < 0)
        {
            return;
        }

        using var context = _contextFactory();
        var info = context.Issues
            .Where(i => i.Id == issueId)
            .Select(i => new { i.FilePath, i.FileSize })
            .FirstOrDefault();

        var old = Issues[index];
        Issues[index] = new IssueCardSample
        {
            Id = old.Id,
            SeriesId = old.SeriesId,
            Title = old.Title,
            IsUnread = old.IsUnread,
            CoverBrush = old.CoverBrush,
            CoverImage = CoverImageCache.Get(issueId, info?.FilePath, info?.FileSize),
            FilePath = old.FilePath,
            IsSelected = old.IsSelected,
        };
    }
}
