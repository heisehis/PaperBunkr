using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.ViewModels.EntityPages;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>A row on the entity page (continuity, event, series). <see cref="Initials"/> is the monogram in the row's thumbnail slot when it has no
/// image; <see cref="MapCommand"/> opens the map featuring the page's character or team (context menu only).</summary>
public sealed record EntityPageLink(int Id, string Name, string Detail, ICommand OpenCommand, string? CoverKey = null, string? ImagePath = null, string Initials = "",
    ICommand? MapCommand = null)
{
    public bool HasMapCommand => MapCommand is not null;
    public bool HasImage => ImagePath is not null;
    public bool HasCover => CoverKey is not null;
    public bool ShowInitials => !HasImage && !HasCover;
}

/// <summary>A continuity segment ("Earth 616 · 30"). <see cref="Name"/> is what the SuggestBox matches; <see cref="Label"/> carries the count.</summary>
public sealed partial class EntityContinuityChoice : ObservableObject
{
    public EntityContinuityChoice(int? id, string name, string label, Action choose) { Id = id; Name = name; Label = label; ChooseCommand = new RelayCommand(choose); }
    public int? Id { get; }
    public string Name { get; }
    public string Label { get; }
    public ICommand ChooseCommand { get; }
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// The entity page for characters, teams, locations, creators and publishers (docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md):
/// a header, a filter (continuity, roles or imprints) and tabs, with one <see cref="IEntityPageKind"/> profile per variation. Data loads on a worker
/// with a fresh context; generation guards keep identity and filter changes safe from late results.
/// </summary>
public partial class MetadataEntityDetailScreenViewModel : ViewModelBase, IContextMenuProvider
{
    private const int StripSize = 12;
    private const int PublisherCharacters = 48;
    private const int EventPreview = 3;

    private readonly Action _navigateBack;
    private readonly Action<int> _goSeries;
    private readonly Action<int> _goIssue;
    private readonly Func<PaperbunkrDbContext> _contexts;
    private readonly Func<ComicMetadataEntityKind, int, CancellationToken, Task<EntityProfileOutcome>> _complete;
    private readonly Func<Action, Task> _dispatch;
    private CancellationTokenSource? _cancellation;
    private int _generation;
    private EntityPage? _page;
    private int? _continuityId;
    private string? _role;
    private string? _imprint;
    private IReadOnlyDictionary<int, string> _imprints = new Dictionary<int, string>();
    private IReadOnlyList<EntityContinuityChoice> _choices = Array.Empty<EntityContinuityChoice>();

    public MetadataEntityDetailScreenViewModel(Action navigateBack, Action<int> goDetailForSeries, Action<int> goReaderForIssue,
        Func<PaperbunkrDbContext>? contextFactory = null,
        Func<ComicMetadataEntityKind, int, CancellationToken, Task<EntityProfileOutcome>>? complete = null, Func<Action, Task>? dispatch = null)
    {
        _navigateBack = navigateBack; _goSeries = goDetailForSeries; _goIssue = goReaderForIssue;
        _contexts = contextFactory ?? PaperbunkrDb.CreateContext;
        _complete = complete ?? new EntityProfileCompletion(_contexts, priority: Paperbunkr.Data.ComicVine.ComicVineRequestPriority.High).CompleteAsync;
        _dispatch = dispatch ?? (async action => { if (Dispatcher.UIThread.CheckAccess()) action(); else await Dispatcher.UIThread.InvokeAsync(action); });
        OpenTileCommand = new RelayCommand<EntityTileItem?>(tile => { if (tile is not null) OpenEntity?.Invoke(tile.Kind, tile.Id, _continuityId); });
    }

    public Action<int, ReadingQueue>? ReadQueue { get; set; }
    public Action<int>? OpenContinuity { get; set; }
    public Action<int>? OpenEvent { get; set; }

    /// <summary>Opens a continuity's / an event's map featuring this character or team (continuity map M5 §5).</summary>
    public Action<int, ComicMetadataEntityKind, int>? OpenContinuityMap { get; set; }
    public Action<int, ComicMetadataEntityKind, int>? OpenEventMap { get; set; }
    public Action<int>? OpenReadingList { get; set; }
    public Action<string>? ShowInLibrary { get; set; }
    public Action<string, string>? Notify { get; set; }

    /// <summary>Opens another entity's page (a tile), keeping the selected continuity.</summary>
    public Action<ComicMetadataEntityKind, int, int?>? OpenEntity { get; set; }

    public ComicMetadataEntityKind Kind { get; private set; }
    public int EntityId { get; private set; }
    public IEntityPageKind Profile { get; private set; } = EntityPageKinds.Character;
    public int? SelectedContinuityId => _continuityId;
    public string KindLabel => Profile.Overline;
    public bool IsPublisher => Kind == ComicMetadataEntityKind.Publisher;
    public bool IsPortraitImage => Profile.ImageShape == EntityImageShape.Portrait;
    public bool IsWideImage => Profile.ImageShape == EntityImageShape.Wide;
    public bool IsSquircleImage => Profile.ImageShape == EntityImageShape.Squircle;
    public bool IsBrandMark => Profile.ImageShape == EntityImageShape.BrandMark;
    public bool ShowsNameText => !IsBrandMark;
    public bool CanChangeImage => EntityProfileCompletion.Supports(Kind);
    public bool HasCustomImage => _page?.CustomImagePath is not null;
    public bool HasAppearances => !IsPublisher && _page?.AppearanceCount > 0;
    public bool IsEmpty => !IsLoading && _page is not null && _page.AppearanceCount == 0 && !IsPublisher;
    public string EmptyMessage => $"No issues in your library feature {Name} yet.";
    public bool HasDescription => !string.IsNullOrWhiteSpace(_page?.Description);
    public string Description => _page?.Description ?? string.Empty;
    public string AliasLabel => string.IsNullOrWhiteSpace(_page?.Aliases) ? string.Empty : $"Also known as {_page.Aliases}";
    public bool HasAliases => AliasLabel.Length > 0;
    public bool HasRoles => Roles.Count > 0;
    public string Counts => _page is null ? string.Empty : Profile.CountsLine(_page);
    public IReadOnlyList<EntityContinuityChoice> ContinuityChoices => _choices;
    public IReadOnlyList<string> ContinuityNames => _choices.Select(c => c.Name).ToList();
    private bool ContinuityFilter => Profile.FilterMode == EntityFilterMode.Continuity;
    public bool UseContinuitySegments => ContinuityFilter && _choices.Count is > 1 and <= 5;
    public bool UseContinuityPicker => ContinuityFilter && _choices.Count > 5;
    public bool ShowRoleChips => Profile.FilterMode == EntityFilterMode.Roles && RoleChips.Count > 2;
    public bool ShowImprintChips => Profile.FilterMode == EntityFilterMode.Imprints && ImprintChips.Count > 1;
    public string Initials => EntityImageResolver.Initials(Name);
    public bool HasImage => ImagePath is not null;
    public bool HasCover => CoverKey is not null;
    public bool ShowInitials => !HasImage && !HasCover && !IsPublisher;
    public int DescriptionMaxLines => IsDescriptionExpanded ? 0 : 2;
    public string DescriptionToggle => IsDescriptionExpanded ? "Show less" : "Show more";
    public Task LoadingTask { get; private set; } = Task.CompletedTask;
    public Task ProfileTask { get; private set; } = Task.CompletedTask;

    // Header actions
    public string PrimaryLabel => Profile.PrimaryAction switch
    {
        EntityPrimaryAction.ShowInLibrary => "Show in Library",
        EntityPrimaryAction.Continue when ContinueIssue is { } next => $"Continue · {next.Label}",
        _ => "Read in order",
    };
    public ICommand PrimaryCommand => Profile.PrimaryAction == EntityPrimaryAction.ShowInLibrary ? ShowLibraryCommand : ReadInOrderCommand;
    public bool ShowSaveAsList => Profile.ShowsSaveAsList;
    public bool ShowSecondaryLibrary => Profile.PrimaryAction != EntityPrimaryAction.ShowInLibrary;
    public bool ShowMapInManage => Profile.ShowsMapAction && MapContinuityId is not null;
    private EntityPageIssue? ContinueIssue => _page?.Appearances.FirstOrDefault(i => !i.IsRead);
    private int? MapContinuityId => _continuityId ?? _page?.Continuities.FirstOrDefault()?.Id;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEmpty))] private bool _isLoading;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string? _errorMessage;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Shown under the header when the picture and bio could not be fetched (provider busy, offline, rate-limited); cleared on the next load.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasProfileNote))] private string? _profileNote;

    public bool HasProfileNote => !string.IsNullOrEmpty(ProfileNote);
    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private long _imageRevision;
    [ObservableProperty] private string? _coverKey;
    [ObservableProperty] private bool _isDescriptionExpanded;
    [ObservableProperty] private string _selectedContinuityName = "All";
    [ObservableProperty] private CoverPickerViewModel? _imagePicker;
    [ObservableProperty] private IReadOnlyList<string> _collageKeys = Array.Empty<string>();

    // Content
    [ObservableProperty] private IReadOnlyList<EntityPageLink> _continuities = Array.Empty<EntityPageLink>();
    [ObservableProperty] private IReadOnlyList<EntityPageLink> _events = Array.Empty<EntityPageLink>();
    [ObservableProperty] private IReadOnlyList<EntityPageLink> _series = Array.Empty<EntityPageLink>();
    [ObservableProperty] private IReadOnlyList<EntityPageLink> _unread = Array.Empty<EntityPageLink>();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFirstAppearance))] private EntityPageLink? _firstAppearance;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasRoles))] private IReadOnlyList<EntityPageRole> _roles = Array.Empty<EntityPageRole>();
    [ObservableProperty] private YearChartModel _yearChart = YearChartModel.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasRelated))] private IReadOnlyList<EntityTileItem> _related = Array.Empty<EntityTileItem>();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCareerRuns))] private IReadOnlyList<CareerRunItem> _careerRuns = Array.Empty<CareerRunItem>();
    [ObservableProperty] private string _careerAxisStart = string.Empty;
    [ObservableProperty] private string _careerAxisEnd = string.Empty;
    [ObservableProperty] private IReadOnlyList<EntityFilterChip> _roleChips = Array.Empty<EntityFilterChip>();
    [ObservableProperty] private IReadOnlyList<EntityFilterChip> _imprintChips = Array.Empty<EntityFilterChip>();
    [ObservableProperty] private IReadOnlyList<EntityPosterItem> _visibleAppearances = Array.Empty<EntityPosterItem>();
    [ObservableProperty] private IReadOnlyList<EntityPosterItem> _seriesCards = Array.Empty<EntityPosterItem>();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EventsShown), nameof(MoreEventsLabel))] private bool _eventsExpanded;
    [ObservableProperty] private AppearanceSort _appearanceSort;
    [ObservableProperty] private bool _unreadOnly;

    public bool HasFirstAppearance => FirstAppearance is not null;
    public bool HasRelated => Related.Count > 0;
    public bool HasCareerRuns => CareerRuns.Count > 0;
    public bool HasEvents => Events.Count > 0;

    /// <summary>The Overview's year card sits beside the events, or takes the left column when there are none (a location usually has none).</summary>
    public int YearCardColumn => HasEvents ? 1 : 0;

    /// <summary>"Often appears with" / "Who's been here" / "Often works with" / a publisher's characters.</summary>
    public string RelatedTitle => Kind switch
    {
        ComicMetadataEntityKind.Location => "Who's been here",
        ComicMetadataEntityKind.Creator => "Often works with",
        _ => "Often appears with",
    };
    public string EventsTitle => Kind == ComicMetadataEntityKind.Location ? "Events set here" : "Events";
    public string YearChartTitle => Kind switch
    {
        ComicMetadataEntityKind.Location => "Seen by year",
        ComicMetadataEntityKind.Publisher => "Published by year",
        ComicMetadataEntityKind.Creator => "Credits by year",
        _ => "Appearances by year",
    };
    public bool RelatedAsSquircles => Kind == ComicMetadataEntityKind.Creator;

    /// <summary>The Overview's event preview: the first few, or every event once a location's list is expanded.</summary>
    public IReadOnlyList<EntityPageLink> EventsShown => EventsExpanded ? Events : Events.Take(EventPreview).ToList();
    public bool HasMoreEvents => Events.Count > EventPreview;
    public string MoreEventsLabel => Kind == ComicMetadataEntityKind.Location
        ? (EventsExpanded ? "Show fewer" : $"Show all {Events.Count:N0}")
        : $"All {Events.Count:N0} events";

    public ICommand OpenTileCommand { get; }

    partial void OnIsDescriptionExpandedChanged(bool value) { OnPropertyChanged(nameof(DescriptionMaxLines)); OnPropertyChanged(nameof(DescriptionToggle)); }
    partial void OnSelectedContinuityNameChanged(string value)
    {
        var choice = _choices.FirstOrDefault(c => c.Name == value);
        if (choice is not null && choice.Id != _continuityId) SelectContinuity(choice.Id);
    }
    partial void OnAppearanceSortChanged(AppearanceSort value) { RebuildAppearances(); NotifySortFlags(); }
    partial void OnUnreadOnlyChanged(bool value) => RebuildAppearances();
    partial void OnEventsChanged(IReadOnlyList<EntityPageLink> value)
    {
        OnPropertyChanged(nameof(HasEvents)); OnPropertyChanged(nameof(YearCardColumn)); OnPropertyChanged(nameof(EventsShown)); OnPropertyChanged(nameof(HasMoreEvents)); OnPropertyChanged(nameof(MoreEventsLabel));
    }
    partial void OnRoleChipsChanged(IReadOnlyList<EntityFilterChip> value) => OnPropertyChanged(nameof(ShowRoleChips));
    partial void OnImprintChipsChanged(IReadOnlyList<EntityFilterChip> value) => OnPropertyChanged(nameof(ShowImprintChips));

    public bool SortsByReadingOrder => AppearanceSort == AppearanceSort.ReadingOrder;
    public bool SortsByNewest => AppearanceSort == AppearanceSort.Newest;
    public bool SortsBySeries => AppearanceSort == AppearanceSort.Series;
    private void NotifySortFlags()
    {
        OnPropertyChanged(nameof(SortsByReadingOrder)); OnPropertyChanged(nameof(SortsByNewest)); OnPropertyChanged(nameof(SortsBySeries));
    }

    public void Load(ComicMetadataEntityKind kind, int entityId, int? continuityId = null)
    {
        Kind = kind; EntityId = entityId; Profile = EntityPageKinds.For(kind); IsDescriptionExpanded = false; EventsExpanded = false;
        _continuityId = continuityId; _role = null; _imprint = null; _imprints = new Dictionary<int, string>();
        UnreadOnly = false; AppearanceSort = AppearanceSort.ReadingOrder;
        SelectedTabId = Profile.TabIds[0];
        StartLoad(clear: true);
    }
    public void SelectContinuity(int? id) { _continuityId = id; StartLoad(clear: false); }
    public void Refresh() => StartLoad(clear: false);

    private void StartLoad(bool clear)
    {
        _cancellation?.Cancel(); _cancellation?.Dispose(); _cancellation = new CancellationTokenSource();
        int generation = ++_generation;
        IsLoading = true; ErrorMessage = null; ProfileNote = null;
        if (clear)
        {
            _page = null; Name = string.Empty; ImagePath = CoverKey = null; _choices = Array.Empty<EntityContinuityChoice>();
            Continuities = Events = Series = Unread = Array.Empty<EntityPageLink>(); FirstAppearance = null;
            YearChart = YearChartModel.Empty; Roles = Array.Empty<EntityPageRole>(); Related = Array.Empty<EntityTileItem>(); CareerRuns = Array.Empty<CareerRunItem>();
            RoleChips = ImprintChips = Array.Empty<EntityFilterChip>(); VisibleAppearances = SeriesCards = Array.Empty<EntityPosterItem>();
            CollageKeys = Array.Empty<string>();
            BuildTabs();
            NotifyPage();
        }
        LoadingTask = LoadPageAsync(Kind, EntityId, _continuityId, _role, generation, _cancellation.Token);
    }

    /// <summary>Everything a load computes off the UI thread.</summary>
    private sealed record LoadedPage(EntityPage Page, IReadOnlyList<EntityTileItem> Related, IReadOnlyList<CareerRun> Career, IReadOnlyDictionary<int, string> Imprints,
        IReadOnlyList<string> Collage, IReadOnlyDictionary<int, string?> SeriesCovers);

    private async Task LoadPageAsync(ComicMetadataEntityKind kind, int id, int? continuityId, string? role, int generation, CancellationToken ct)
    {
        try
        {
            var data = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var context = _contexts();
                var page = EntityPageResolver.Build(context, kind, id, continuityId, role);
                var order = page.ReadingOrder;
                IReadOnlyList<EntityTileItem> related = kind switch
                {
                    ComicMetadataEntityKind.Character => Tiles(EntityRelations.Seen(context, order, StripSize, exclude: (kind, id)), r => $"{r.SharedCount:N0} together"),
                    ComicMetadataEntityKind.Team => Tiles(EntityRelations.Seen(context, order, StripSize, exclude: (kind, id), charactersFirst: true), r => $"{r.SharedCount:N0} together"),
                    ComicMetadataEntityKind.Location => Tiles(EntityRelations.Seen(context, order, StripSize), r => EntityPageText.Count(r.SharedCount, "issue", "issues")),
                    ComicMetadataEntityKind.Creator => Tiles(EntityRelations.Collaborators(context, id, order, StripSize), r => EntityPageText.Count(r.SharedCount, "issue", "issues")),
                    ComicMetadataEntityKind.Publisher => Tiles(EntityRelations.Seen(context, order, PublisherCharacters, includeTeams: false), r => EntityPageText.Count(r.SharedCount, "appearance", "appearances")),
                    _ => Array.Empty<EntityTileItem>(),
                };
                var career = kind == ComicMetadataEntityKind.Creator ? EntityRelations.CreatorCareer(context, id, order) : Array.Empty<CareerRun>();
                var imprints = kind == ComicMetadataEntityKind.Publisher ? EntityRelations.SeriesImprints(context, page.Series.Select(s => s.Id).ToList()) : new Dictionary<int, string>();
                var firstBySeries = new Dictionary<int, int>();
                foreach (var issue in page.Appearances) firstBySeries.TryAdd(issue.SeriesId, issue.Id);
                var seriesCovers = page.Series.ToDictionary(s => s.Id, s => firstBySeries.TryGetValue(s.Id, out int issueId) ? EntityTileItem.CoverKeyFor(issueId) : null);
                return new LoadedPage(page, related, career, imprints, Collage(kind, page, seriesCovers), seriesCovers);
            }, ct);
            await _dispatch(() =>
            {
                if (generation != _generation || ct.IsCancellationRequested) return;
                Apply(data, kind, id);
                IsLoading = false; ProfileTask = CompleteProfileAsync(kind, id, generation, ct);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await _dispatch(() => { if (generation == _generation) { ErrorMessage = ex.Message; IsLoading = false; } }); }
    }

    private static IReadOnlyList<EntityTileItem> Tiles(IReadOnlyList<EntityRelation> relations, Func<EntityRelation, string> caption) =>
        relations.Select(r => EntityTileItem.From(r, caption(r))).ToList();

    /// <summary>The header collage (spec Q14): the five most recent appearance covers, or for a publisher one cover from each of the five series
    /// you own the most issues of. Only covers that are cached, so the band never shows blanks.</summary>
    private static IReadOnlyList<string> Collage(ComicMetadataEntityKind kind, EntityPage page, IReadOnlyDictionary<int, string?> seriesCovers)
    {
        if (kind == ComicMetadataEntityKind.Publisher)
            return page.Series.OrderByDescending(s => s.Count).Select(s => seriesCovers.GetValueOrDefault(s.Id)).OfType<string>().Take(5).ToList();
        return page.Appearances.AsEnumerable().Reverse().Select(i => EntityTileItem.CoverKeyFor(i.Id)).OfType<string>().Take(5).ToList();
    }

    private async Task CompleteProfileAsync(ComicMetadataEntityKind kind, int id, int generation, CancellationToken ct)
    {
        if (!EntityProfileCompletion.Supports(kind)) return;
        try
        {
            var outcome = await Task.Run(() => _complete(kind, id, ct), ct);
            if (outcome == EntityProfileOutcome.NoSource)
            {
                await _dispatch(() =>
                {
                    if (generation == _generation && !ct.IsCancellationRequested)
                        ProfileNote = "Not linked to Metron or ComicVine yet, so there's no picture or bio to fetch. Scraping one of its issues links it.";
                });
                return;
            }

            if (outcome == EntityProfileOutcome.Failed)
            {
                await _dispatch(() =>
                {
                    if (generation == _generation && !ct.IsCancellationRequested)
                        ProfileNote = "Couldn't fetch the picture and bio just now (the source may be busy or offline). It tries again next time you open this page.";
                });
                return;
            }

            if (outcome != EntityProfileOutcome.Checked) return;
            using var context = _contexts();
            var profile = EntityProfileCompletion.Find(context, kind, id);
            if (profile is null) return;
            await _dispatch(() =>
            {
                if (generation != _generation || ct.IsCancellationRequested || _page is null) return;
                _page = _page with { Description = profile.Description, Aliases = profile.Aliases, ImageUrl = profile.ImageUrl, CustomImagePath = profile.CustomImagePath };
                ApplyImage(_page, kind, id);
                NotifyPage();
            });
        }
        catch (OperationCanceledException) { }
        catch { /* Provider refresh is optional; keep the local page usable. */ }
    }

    private void Apply(LoadedPage data, ComicMetadataEntityKind kind, int id)
    {
        var page = data.Page;
        _page = page; Name = page.Name; _imprints = data.Imprints;
        _choices = new[] { new EntityContinuityChoice(null, "All", "All", () => SelectContinuity(null)) }
            .Concat(page.Continuities.Select(c => new EntityContinuityChoice(c.Id, c.Name, $"{c.Name} · {c.Count:N0}", () => SelectContinuity(c.Id)))).ToList();
        foreach (var choice in _choices) choice.IsSelected = choice.Id == _continuityId;
        SelectedContinuityName = _choices.FirstOrDefault(c => c.Id == _continuityId)?.Name ?? "All";
        bool map = Profile.ShowsMapAction;
        Continuities = page.Continuities.Where(c => _continuityId == null || c.Id == _continuityId)
            .Select(c => new EntityPageLink(c.Id, c.Name, EntityPageText.Count(c.Count, Profile.IssueNoun(1), Profile.IssueNoun(2)), new RelayCommand(() => OpenContinuity?.Invoke(c.Id)),
                Initials: EntityImageResolver.Initials(c.Name), MapCommand: map ? new RelayCommand(() => OpenContinuityMap?.Invoke(c.Id, Kind, EntityId)) : null)).ToList();
        Events = page.Events.Select(e =>
        {
            string cover = StoryEventCoverPaths.GetPath(e.Id);
            return new EntityPageLink(e.Id, e.Name, EntityPageText.JoinDetail(e.Year?.ToString() ?? "Undated", e.Continuity, EntityPageText.Count(e.Count, "issue", "issues")),
                new RelayCommand(() => OpenEvent?.Invoke(e.Id)), ImagePath: System.IO.File.Exists(cover) ? cover : null, Initials: EntityImageResolver.Initials(e.Name),
                MapCommand: map ? new RelayCommand(() => OpenEventMap?.Invoke(e.Id, Kind, EntityId)) : null);
        }).ToList();
        Series = page.Series.Select(s => new EntityPageLink(s.Id, s.Name,
            EntityPageText.JoinDetail(EntityPageText.Count(s.Count, Profile.IssueNoun(1), Profile.IssueNoun(2)), $"{s.ReadCount:N0} read"), new RelayCommand(() => _goSeries(s.Id)))).ToList();
        EntityPageLink Link(EntityPageIssue i) => new(i.Id, i.Label, EntityPageText.JoinDetail(i.Year?.ToString() ?? "Undated", i.IsRead ? "Read" : "Unread"), new RelayCommand(() => _goIssue(i.Id)), i.Id.ToString());
        FirstAppearance = page.FirstAppearance is { } first ? Link(first) : null;
        Unread = page.Unread.Select(Link).ToList();
        YearChart = YearChartModel.From(page.ByYear);
        Roles = page.Roles.Select(r => r with { Role = EntityPageText.RoleName(r.Role) }).ToList();
        Related = data.Related;
        CollageKeys = data.Collage;
        BuildRoleChips(page);
        BuildImprintChips(page);
        BuildCareer(data.Career);
        RebuildAppearances();
        RebuildSeriesCards(data.SeriesCovers);
        ApplyImage(page, kind, id);
        UpdateTabs();
        NotifyPage();
    }

    private void ApplyImage(EntityPage page, ComicMetadataEntityKind kind, int id)
    {
        var image = kind is ComicMetadataEntityKind.Publisher
            ? new EntityImageChoice(EntityImageKind.Initials, null, null, Initials)
            : EntityImageResolver.Choose(page.Name, page.CustomImagePath, page.ImageUrl, page.FirstAppearance?.Id);
        ImagePath = image.Path; ImageRevision++;
        CoverKey = image.CoverIssueId is int coverId && CoverThumbnailService.GetEffectiveCoverPath(coverId) is not null ? coverId.ToString() : null;
        if (CanChangeImage) ImagePicker = new CoverPickerViewModel(page.Appearances.Select(i => new CoverPickerCandidate(i.Id, i.Label)).ToList(),
            path => SetCustomImage(kind, id, path), () => Dispatcher.UIThread.Post(Refresh));
    }

    private void BuildRoleChips(EntityPage page)
    {
        if (Profile.FilterMode != EntityFilterMode.Roles) { RoleChips = Array.Empty<EntityFilterChip>(); return; }
        var chips = new List<EntityFilterChip> { new(null, "All", ChooseRole) };
        chips.AddRange(page.Roles.Select(r => new EntityFilterChip(r.Role, $"{EntityPageText.RoleName(r.Role)} {r.Count:N0}", ChooseRole)));
        foreach (var chip in chips) chip.IsSelected = chip.Value == _role;
        RoleChips = chips;
    }

    private void ChooseRole(EntityFilterChip chip)
    {
        if (chip.Value == _role) return;
        _role = chip.Value;
        foreach (var other in RoleChips) other.IsSelected = other.Value == _role;
        StartLoad(clear: false);
    }

    private void BuildImprintChips(EntityPage page)
    {
        if (Profile.FilterMode != EntityFilterMode.Imprints || _imprints.Count == 0) { ImprintChips = Array.Empty<EntityFilterChip>(); return; }
        var chips = new List<EntityFilterChip> { new(null, "All imprints", ChooseImprint) };
        chips.AddRange(_imprints.Values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new EntityFilterChip(g.Key, $"{g.Key} {g.Count():N0}", ChooseImprint)));
        if (_imprint is not null && chips.All(c => !string.Equals(c.Value, _imprint, StringComparison.OrdinalIgnoreCase))) _imprint = null;
        foreach (var chip in chips) chip.IsSelected = string.Equals(chip.Value, _imprint, StringComparison.OrdinalIgnoreCase);
        ImprintChips = chips;
    }

    private void ChooseImprint(EntityFilterChip chip)
    {
        _imprint = chip.Value;
        foreach (var other in ImprintChips) other.IsSelected = string.Equals(other.Value, _imprint, StringComparison.OrdinalIgnoreCase);
        RebuildSeriesCards(null);
        UpdateTabs();
    }

    private IReadOnlyDictionary<int, string?> _seriesCovers = new Dictionary<int, string?>();

    private void RebuildSeriesCards(IReadOnlyDictionary<int, string?>? covers)
    {
        if (covers is not null) _seriesCovers = covers;
        if (_page is null) { SeriesCards = Array.Empty<EntityPosterItem>(); return; }
        var series = _page.Series.Where(s => _imprint is null || (_imprints.TryGetValue(s.Id, out var imprint) && string.Equals(imprint, _imprint, StringComparison.OrdinalIgnoreCase)));
        SeriesCards = series.Select(s => new EntityPosterItem(s.Id, s.Name,
            s.Count == 0 ? "None in your library" : EntityPageText.JoinDetail(EntityPageText.Count(s.Count, Profile.IssueNoun(1), Profile.IssueNoun(2)), $"{s.ReadCount:N0} read"),
            _seriesCovers.GetValueOrDefault(s.Id), s.Count == 0 ? null : (double)s.ReadCount / s.Count, new RelayCommand(() => _goSeries(s.Id)), IsSeries: true)).ToList();
    }

    private void RebuildAppearances()
    {
        if (_page is null) { VisibleAppearances = Array.Empty<EntityPosterItem>(); return; }
        IEnumerable<EntityPageIssue> issues = _page.Appearances;
        if (UnreadOnly) issues = issues.Where(i => !i.IsRead);
        issues = AppearanceSort switch
        {
            AppearanceSort.Newest => issues.Reverse(),
            AppearanceSort.Series => issues.Select((issue, index) => (issue, index)).OrderBy(x => x.issue.SeriesName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.index).Select(x => x.issue),
            _ => issues,
        };
        VisibleAppearances = issues.Select(i => new EntityPosterItem(i.Id, i.Label, EntityPageText.JoinDetail(i.Year?.ToString() ?? "Undated", i.IsRead ? "Read" : "Unread"),
            i.Id.ToString(), null, new RelayCommand(() => _goIssue(i.Id)))).ToList();
    }

    private void BuildCareer(IReadOnlyList<CareerRun> runs)
    {
        var years = runs.SelectMany(r => new[] { r.FirstYear, r.LastYear }).OfType<int>().ToList();
        int first = years.Count > 0 ? years.Min() : 0, last = years.Count > 0 ? years.Max() : 0;
        double span = Math.Max(1, last - first + 1);
        CareerAxisStart = years.Count > 0 ? first.ToString() : string.Empty;
        CareerAxisEnd = years.Count > 0 && last != first ? last.ToString() : string.Empty;
        CareerRuns = runs.Select(r =>
        {
            double start = r.FirstYear is int a ? (a - first) / span : 0;
            double end = r.LastYear is int b ? (b - first + 1) / span : 1;
            string subtitle = EntityPageText.JoinDetail(EntityPageText.RoleName(r.Role), r.Range, EntityPageText.YearSpan(r.FirstYear, r.LastYear));
            var ids = r.IssueIds;
            string title = $"{r.Series} ({EntityPageText.RoleName(r.Role)} {r.Range})";
            return new CareerRunItem(r.Series, subtitle, $"{r.ReadCount:N0} of {ids.Count:N0} read", start, end,
                string.Equals(r.Role, "Writer", StringComparison.OrdinalIgnoreCase),
                new RelayCommand(() => ReadOrder(ids, title)), new RelayCommand(() => SaveOrder(ids, title)));
        }).ToList();
    }

    public bool SetCustomImage(ComicMetadataEntityKind kind, int id, string path)
    {
        string? file = EntityImageCache.TrySetCustom(kind, id, path);
        if (file is null) return false;
        using var context = _contexts();
        var entity = EntityProfileCompletion.Find(context, kind, id);
        if (entity is null) return false;
        entity.CustomImagePath = file; entity.ImageSource = EntityImageSource.Custom; context.SaveChanges();
        EntityChipImages.RaiseChanged();
        return true;
    }

    private void NotifyPage()
    {
        foreach (string property in new[] { nameof(Profile), nameof(KindLabel), nameof(IsPublisher), nameof(IsPortraitImage), nameof(IsWideImage), nameof(IsSquircleImage),
            nameof(IsBrandMark), nameof(ShowsNameText), nameof(CanChangeImage), nameof(HasCustomImage), nameof(HasAppearances), nameof(IsEmpty), nameof(EmptyMessage),
            nameof(Description), nameof(HasDescription), nameof(AliasLabel), nameof(HasAliases), nameof(Counts), nameof(Initials), nameof(HasImage), nameof(HasCover),
            nameof(ShowInitials), nameof(ContinuityChoices), nameof(ContinuityNames), nameof(UseContinuitySegments), nameof(UseContinuityPicker), nameof(SelectedContinuityId),
            nameof(ShowRoleChips), nameof(ShowImprintChips), nameof(PrimaryLabel), nameof(PrimaryCommand), nameof(ShowSaveAsList), nameof(ShowSecondaryLibrary),
            nameof(ShowMapInManage), nameof(RelatedTitle), nameof(EventsTitle), nameof(YearChartTitle), nameof(RelatedAsSquircles), nameof(MoreEventsLabel) })
            OnPropertyChanged(property);
        ReadInOrderCommand.NotifyCanExecuteChanged(); SaveAsReadingListCommand.NotifyCanExecuteChanged();
    }

    private void ReadOrder(IReadOnlyList<int> ids, string label)
    {
        using var context = _contexts();
        var issues = context.Issues.Where(i => ids.Contains(i.Id) && !i.FileIsMissing && !i.IsPlaceholder).ToDictionary(i => i.Id);
        int? first = ids.Where(issues.ContainsKey).Select(i => (int?)i).FirstOrDefault(id => !issues[id!.Value].HasBeenRead());
        first ??= ids.Where(issues.ContainsKey).Select(i => (int?)i).FirstOrDefault();
        if (first is int issueId) ReadQueue?.Invoke(issueId, new ReadingQueue(ids.ToArray(), label));
    }

    private void SaveOrder(IReadOnlyList<int> ids, string label)
    {
        try { using var context = _contexts(); int listId = OrderedIssueLists.Save(context, label, ids); OpenReadingList?.Invoke(listId); }
        catch (Exception ex) { Notify?.Invoke("Couldn't save the reading list", ex.Message); }
    }

    /// <summary>Reads the page's issues in order, starting at the first unread one ("Continue" on characters and teams).</summary>
    [RelayCommand(CanExecute = nameof(HasAppearances))] private void ReadInOrder() { if (_page is { } page) ReadOrder(page.ReadingOrder, Name); }
    [RelayCommand(CanExecute = nameof(HasAppearances))] private void SaveAsReadingList() { if (_page is { } page) SaveOrder(page.ReadingOrder, Name); }
    [RelayCommand] private void NavigateBack() => _navigateBack();
    [RelayCommand] private void ShowLibrary() => ShowInLibrary?.Invoke(Name);
    [RelayCommand] private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;
    [RelayCommand] private void RefreshProfile() => Refresh();
    [RelayCommand] private void OpenOnMap() { if (MapContinuityId is int continuity) OpenContinuityMap?.Invoke(continuity, Kind, EntityId); }
    [RelayCommand] private void SortAppearances(AppearanceSort sort) => AppearanceSort = sort;

    /// <summary>"All N events" (characters, teams) switches to the Events tab; a location's list expands in place (it has no Events tab).</summary>
    [RelayCommand] private void ShowMoreEvents()
    {
        if (Kind == ComicMetadataEntityKind.Location) EventsExpanded = !EventsExpanded;
        else SelectTab(EntityTabIds.Events);
    }

    [RelayCommand] private void ResetImage()
    {
        if (!CanChangeImage) return;
        using var context = _contexts();
        var entity = EntityProfileCompletion.Find(context, Kind, EntityId);
        if (entity is null) return;
        string? custom = entity.CustomImagePath;
        entity.CustomImagePath = null; entity.ImageSource = EntityImageSource.None; context.SaveChanges();
        EntityImageCache.ClearCustom(custom); EntityChipImages.RaiseChanged(); Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>Right-click / Menu key on a tile, an event row or a continuity row (the shared ContextMenuHost).</summary>
    public IReadOnlyList<ContextMenuEntry>? BuildContextMenu(object? target)
    {
        switch (target)
        {
            case EntityTileItem tile:
                var entries = new List<ContextMenuEntry> { ContextMenuEntry.Item($"Open {tile.Name}", OpenTileCommand, tile) };
                if (tile.CanOpenOnMap && MapContinuityId is int continuity)
                    entries.Add(ContextMenuEntry.Item("Open on the map", new RelayCommand(() => OpenContinuityMap?.Invoke(continuity, tile.Kind, tile.Id))));
                entries.Add(ContextMenuEntry.Item("Show in Library", new RelayCommand(() => ShowInLibrary?.Invoke(tile.Name))));
                return entries;
            case EntityPageLink link:
                var rows = new List<ContextMenuEntry> { ContextMenuEntry.Item($"Open {link.Name}", link.OpenCommand) };
                if (link.MapCommand is { } mapCommand) rows.Add(ContextMenuEntry.Item("Open on the map", mapCommand));
                return rows;
            default:
                return null;
        }
    }
}
