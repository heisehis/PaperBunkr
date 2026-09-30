using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels.Home;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using SkiaSharp;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// App launch screen (docs/superpowers/specs/2026-08-18-home-screen-design.md, reworked by the 2026-09-28 Home pitch:
/// docs/superpowers/specs/2026-09-28-home-improvements-design.md and 2026-09-28-home-cosmetics-design.md). Coordinator for a
/// masthead (time-of-day greeting, light-theme sky or dark-theme cover wall, seasonal flourish, search) plus an ordered,
/// user-configurable list of <see cref="Sections"/>. Every module is re-queried fresh on every visit, off the UI thread, and a
/// hidden section is never queried at all. Query/pick logic lives in <see cref="HomeFeedResolver"/> /
/// <see cref="RecommendationResolver"/>; this ViewModel only maps results into display cards and wires navigation.
/// </summary>
public partial class HomeScreenViewModel : ViewModelBase
{
    /// <summary>Spotlight auto-advance cadence - long enough to read the open panel before the next one opens.</summary>
    private static readonly TimeSpan SpotlightAdvanceInterval = TimeSpan.FromSeconds(7);

    /// <summary>Accordion panels (docs/superpowers/specs/2026-09-29-home-spotlight-accordion-design.md) - eight fill Home's column
    /// with the open panel at 400px; fewer looked thin.</summary>
    public const int SpotlightPickCount = 8;

    private readonly Action<int> _goDetailForSeries;
    private readonly Action<int> _goReaderForIssue;
    private readonly Action<string> _goLibraryWithSearch;
    private readonly Action<int, int> _goReaderForIssueInReadingList;
    private readonly Action<int, BookFormat> _goReaderForBook;
    private readonly Action<int> _goLibraryWithCollection;
    private readonly Action _goInsights;
    private readonly Action<string> _goPreferencesAnchor;
    private readonly IToastHost? _toastHost;
    private readonly Func<DateTime> _localNow;
    private readonly Random _random = new();
    private readonly DispatcherTimer _spotlightTimer;
    private readonly DispatcherTimer _phaseTimer;
    private readonly ThemeService _themeService;
    private readonly Dictionary<string, HomeSectionViewModel> _sectionsByKey;

    private IReadOnlyList<string> _visibleKeys = HomeSectionKey.Default;
    private bool _seasonalEnabled;
    private bool _hoverPaused;
    private bool _onScreen = true;

    public HomeScreenViewModel(Action<int> goDetailForSeries, Action<int> goReaderForIssue, Action<string> goLibraryWithSearch,
        Action<int, int> goReaderForIssueInReadingList, Action<int, BookFormat> goReaderForBook, Action<int>? goLibraryWithCollection = null,
        ThemeService? themeService = null, bool loadOnConstruction = true, IToastHost? toastHost = null, Action? goInsights = null,
        Action<string>? goPreferencesAnchor = null, Func<DateTime>? localNow = null)
    {
        _goDetailForSeries = goDetailForSeries;
        _goReaderForIssue = goReaderForIssue;
        _goLibraryWithSearch = goLibraryWithSearch;
        _goReaderForIssueInReadingList = goReaderForIssueInReadingList;
        _goReaderForBook = goReaderForBook;
        _goLibraryWithCollection = goLibraryWithCollection ?? (_ => { });
        _goInsights = goInsights ?? (() => { });
        _goPreferencesAnchor = goPreferencesAnchor ?? (_ => { });
        _toastHost = toastHost;
        _localNow = localNow ?? (() => DateTime.Now);
        // Production always passes MainViewModel's shared instance so ThemeApplied actually fires on a Preferences theme switch.
        _themeService = themeService ?? new ThemeService();
        _themeService.ThemeApplied += OnThemeApplied;

        ContinueReading = new ObservableCollection<HomeResumeCard>();
        RecentlyAdded = new ObservableCollection<SeriesCardSample>();
        BecauseYouRead = new ObservableCollection<BecauseYouReadRow>();
        SpotlightItems = new ObservableCollection<SpotlightIssueSample>();
        SpotlightPanels = new ObservableCollection<SpotlightPanelViewModel>();
        Collections = new ObservableCollection<HomeCollectionCard>();
        Sections = new ObservableCollection<HomeSectionViewModel>();

        _sectionsByKey = new Dictionary<string, HomeSectionViewModel>
        {
            [HomeSectionKey.Spotlight] = new SpotlightSectionViewModel(this),
            [HomeSectionKey.NeedsAttention] = new NeedsAttentionSectionViewModel(this),
            [HomeSectionKey.ContinueReading] = new ContinueReadingSectionViewModel(this),
            [HomeSectionKey.RecentlyAdded] = new RecentlyAddedSectionViewModel(this),
            [HomeSectionKey.Collections] = new CollectionsSectionViewModel(this),
            [HomeSectionKey.BecauseYouRead] = new BecauseYouReadSectionViewModel(this),
            [HomeSectionKey.ReadingList] = new ReadingListSectionViewModel(this),
        };

        // Home is a singleton for the app's lifetime (no IDisposable, same as ReaderScreenViewModel's timers). The rotation timer
        // runs only while Home is on screen and not hover-paused (C7) - see UpdateSpotlightTimer.
        _spotlightTimer = new DispatcherTimer { Interval = SpotlightAdvanceInterval };
        _spotlightTimer.Tick += (_, _) => AdvanceSpotlight();
        _spotlightTimer.Start();

        // One-shot, re-armed for the next day-phase boundary so the greeting/sky change while Home is being looked at (C2).
        _phaseTimer = new DispatcherTimer();
        _phaseTimer.Tick += (_, _) =>
        {
            _phaseTimer.Stop();
            UpdateMasthead();
        };


        UpdateMasthead();

        // Production (MainViewModel) passes loadOnConstruction: false - Home is only ever shown after an explicit load, and
        // loading here too ran on the UI thread while the splash was still up. Tests default to true.
        if (loadOnConstruction)
        {
            LoadFromDatabase();
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Sections (I1)
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>The visible sections in the user's order (docs/superpowers/specs/2026-09-28-home-improvements-design.md I1).
    /// Needs Attention drops out when it has nothing to show.</summary>
    public ObservableCollection<HomeSectionViewModel> Sections { get; }

    /// <summary>The user switched every section off - Home shows one line pointing at Preferences instead.</summary>
    [ObservableProperty]
    private bool _allSectionsHidden;

    [RelayCommand]
    private void OpenHomePreferences() => _goPreferencesAnchor("appearance.home");

    private void RebuildSections()
    {
        var wanted = _visibleKeys
            .Where(k => k != HomeSectionKey.NeedsAttention || NeedsAttention is not null)
            .Select(k => _sectionsByKey[k])
            .ToList();

        // Only touch the collection when the order actually changed - a clear-and-refill recreates every section's
        // containers, replaying scroll-reveal and dropping the hero layers mid-crossfade on an ordinary revisit.
        if (!Sections.SequenceEqual(wanted))
        {
            ReplaceAll(Sections, wanted);
        }

        AllSectionsHidden = _visibleKeys.Count == 0;
    }

    private bool IsVisible(string key) => _visibleKeys.Contains(key);

    // ---------------------------------------------------------------------------------------------------------------------
    // Masthead: greeting, sky, seasonal flourish (C2, C10)
    // ---------------------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    private string _greeting = string.Empty;

    /// <summary>Light theme active: the masthead shows the time-of-day sky instead of the cover wall and spotlight tint.</summary>
    [ObservableProperty]
    private bool _isLightTheme;

    [ObservableProperty]
    private Color _skyTopColor;

    [ObservableProperty]
    private Color _skyBottomColor;

    /// <summary>Night sky on a light theme - the top of the gradient is dark, so the masthead text flips to light.</summary>
    [ObservableProperty]
    private bool _useLightMastheadText;

    [ObservableProperty]
    private bool _hasSeason;

    [ObservableProperty]
    private FluentIcons.Common.Symbol _seasonIcon = FluentIcons.Common.Symbol.Star;

    [ObservableProperty]
    private string? _seasonLabel;

    /// <summary>Greeting text colour: the season's tint when one is active, light text on a night sky, else the skin's text colour.</summary>
    [ObservableProperty]
    private IBrush _greetingBrush = Brushes.White;

    /// <summary>Subtitle colour under the greeting - lighter muted text on a night sky, else the skin's muted text.</summary>
    [ObservableProperty]
    private IBrush _mastheadSubtitleBrush = Brushes.Gray;

    /// <summary>Recomputes everything time- or theme-dependent on the masthead and re-arms the phase timer. UI thread.</summary>
    private void UpdateMasthead()
    {
        DateTime now = _localNow();
        var phase = DayPhase.For(now);
        Greeting = DayPhase.Greeting(phase);
        IsLightTheme = ThemeService.IsLightThemeActive;
        SkyTopColor = ResourceColor($"PbSky{phase}TopColor", Colors.LightSkyBlue);
        SkyBottomColor = ResourceColor($"PbSky{phase}BottomColor", Colors.White);
        UseLightMastheadText = IsLightTheme && phase == DayPhaseKind.Night;

        var season = _seasonalEnabled ? SeasonalCalendar.ActiveOn(DateOnly.FromDateTime(now)) : null;
        HasSeason = season is not null;
        Color text = UseLightMastheadText ? ResourceColor("PbSkyNightTextColor", Colors.White) : ResourceColor("PbTextColor", Colors.White);
        Color muted = UseLightMastheadText
            ? ResourceColor("PbSkyNightMutedTextColor", Colors.WhiteSmoke)
            : ResourceColor("PbTextMutedColor", Colors.Gray);
        MastheadSubtitleBrush = new SolidColorBrush(muted);

        if (season is Season active)
        {
            SeasonIcon = SeasonalCalendar.Icon(active);
            SeasonLabel = SeasonalCalendar.Label(active);
            GreetingBrush = new SolidColorBrush(ResourceColor(SeasonalCalendar.TintResourceKey(active), Colors.Goldenrod));
        }
        else
        {
            SeasonLabel = null;
            GreetingBrush = new SolidColorBrush(text);
        }

        _phaseTimer.Stop();
        var wait = DayPhase.NextBoundary(now) - now;
        _phaseTimer.Interval = wait > TimeSpan.Zero ? wait + TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
        _phaseTimer.Start();
    }

    private static Color ResourceColor(string key, Color fallback)
        => Application.Current is { } app && app.TryGetResource(key, null, out var value) && value is Color color ? color : fallback;

    // ---------------------------------------------------------------------------------------------------------------------
    // Spotlight (C1, C7, I4)
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>The blurred "cover-wall" behind the masthead (dark themes) - null until the first load or on a coverless library.</summary>
    [ObservableProperty]
    private Bitmap? _mastheadBackdrop;

    /// <summary>The current spotlight cover's dominant vibrant colour (C1), tinting the dark-theme masthead.</summary>
    [ObservableProperty]
    private Color _spotlightAccentColor = SpotlightAccentSampler.FallbackColor;

    /// <summary>Staggered shelf-card entrance - one-shot per trigger, read on container preparation, never replayed by
    /// virtualization recycling. HomeScreen.axaml.cs flips it once the view is attached.</summary>
    [ObservableProperty]
    private bool _playEntranceAnimation;

    public ObservableCollection<SpotlightIssueSample> SpotlightItems { get; }

    /// <summary>The accordion's panels (docs/superpowers/specs/2026-09-29-home-spotlight-accordion-design.md) - one per
    /// <see cref="SpotlightItems"/> entry, <see cref="SpotlightPanelViewModel.IsOpen"/> following <see cref="SpotlightIndex"/>.</summary>
    public ObservableCollection<SpotlightPanelViewModel> SpotlightPanels { get; }

    [ObservableProperty]
    private int _spotlightIndex;

    public SpotlightIssueSample? CurrentSpotlight =>
        SpotlightIndex >= 0 && SpotlightIndex < SpotlightItems.Count ? SpotlightItems[SpotlightIndex] : null;

    partial void OnSpotlightIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentSpotlight));
        UpdateSpotlightAccentColor();
        SyncOpenPanel();
    }

    private void SyncOpenPanel()
    {
        for (int i = 0; i < SpotlightPanels.Count; i++)
        {
            SpotlightPanels[i].IsOpen = i == SpotlightIndex;
        }
    }

    /// <summary>A click (or Enter) on a panel: opens a closed one, and opens the reader for the one already open - the same thing
    /// clicking the old hero did.</summary>
    [RelayCommand]
    private void ActivateSpotlightPanel(SpotlightPanelViewModel? panel)
    {
        if (panel is null)
        {
            return;
        }

        int index = SpotlightPanels.IndexOf(panel);
        if (index < 0)
        {
            return;
        }

        if (index == SpotlightIndex)
        {
            _goReaderForIssue(panel.Sample.IssueId);
        }
        else
        {
            SpotlightIndex = index;
        }
    }

    /// <summary>"Read now" inside a panel - always the reader, whichever panel it's on.</summary>
    [RelayCommand]
    private void ReadSpotlightPanel(SpotlightPanelViewModel? panel)
    {
        if (panel is not null)
        {
            _goReaderForIssue(panel.Sample.IssueId);
        }
    }

    [RelayCommand]
    private void NextSpotlight() => AdvanceSpotlight();

    [RelayCommand]
    private void PreviousSpotlight()
    {
        if (SpotlightItems.Count > 0)
        {
            SpotlightIndex = (SpotlightIndex - 1 + SpotlightItems.Count) % SpotlightItems.Count;
        }
    }

    private void UpdateSpotlightAccentColor()
    {
        SpotlightAccentColor = CurrentSpotlight?.CoverImage is { } cover
            ? SpotlightAccentSampler.Sample(cover)
            : SpotlightAccentSampler.FallbackColor;
    }

    private void AdvanceSpotlight()
    {
        if (SpotlightItems.Count == 0)
        {
            return;
        }

        SpotlightIndex = (SpotlightIndex + 1) % SpotlightItems.Count;
    }

    /// <summary>Hovering the hero or its dots pauses rotation; leaving resumes it (unless Home is also off screen).</summary>
    public void PauseSpotlightRotation()
    {
        _hoverPaused = true;
        UpdateSpotlightTimer();
    }

    public void ResumeSpotlightRotation()
    {
        _hoverPaused = false;
        UpdateSpotlightTimer();
    }

    /// <summary>Called by the view on attach/detach and visibility flips (C7): no rotation while nobody can see it. Tracked
    /// separately from the hover pause so re-attaching never undoes a pause the pointer still holds.</summary>
    public void SetOnScreen(bool onScreen)
    {
        _onScreen = onScreen;
        UpdateSpotlightTimer();
    }

    public bool IsSpotlightRotating => _spotlightTimer.IsEnabled;

    private void UpdateSpotlightTimer()
    {
        if (_onScreen && !_hoverPaused)
        {
            _spotlightTimer.Start();
        }
        else
        {
            _spotlightTimer.Stop();
        }
    }

    [RelayCommand]
    private void SetSpotlightItem(SpotlightIssueSample? item)
    {
        if (item is null)
        {
            return;
        }

        int index = SpotlightItems.IndexOf(item);
        if (index >= 0)
        {
            SpotlightIndex = index;
        }
    }

    [RelayCommand]
    private void OpenSpotlight()
    {
        if (CurrentSpotlight is not null)
        {
            _goReaderForIssue(CurrentSpotlight.IssueId);
        }
    }

    public bool HasSpotlight => SpotlightItems.Count > 0;

    // ---------------------------------------------------------------------------------------------------------------------
    // Needs Attention (I3)
    // ---------------------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    private HomeAttentionCard? _needsAttention;

    public bool HasNeedsAttention => NeedsAttention is not null;

    partial void OnNeedsAttentionChanged(HomeAttentionCard? value) => OnPropertyChanged(nameof(HasNeedsAttention));

    [RelayCommand]
    private void OpenAttention()
    {
        if (NeedsAttention?.Attention is not { } attention)
        {
            return;
        }

        if (attention.ResumeIssueId is int issueId)
        {
            _goReaderForIssue(issueId);
        }
        else
        {
            _goDetailForSeries(attention.SeriesId);
        }
    }

    [RelayCommand]
    private void OpenInsights() => _goInsights();

    // ---------------------------------------------------------------------------------------------------------------------
    // Shelves
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Comics and books in one row, last-read first (I2).</summary>
    public ObservableCollection<HomeResumeCard> ContinueReading { get; }

    public ObservableCollection<SeriesCardSample> RecentlyAdded { get; }

    public ObservableCollection<BecauseYouReadRow> BecauseYouRead { get; }

    /// <summary>Non-empty collections, capped, in the user's own sidebar order.</summary>
    public ObservableCollection<HomeCollectionCard> Collections { get; }

    public bool HasContinueReading => ContinueReading.Count > 0;

    public bool HasRecentlyAdded => RecentlyAdded.Count > 0;

    public bool HasBecauseYouRead => BecauseYouRead.Count > 0;

    public bool HasCollections => Collections.Count > 0;

    [ObservableProperty]
    private ReadingListSpotlightSample? _readingListSpotlight;

    public bool HasReadingListSpotlight => ReadingListSpotlight is not null;

    partial void OnReadingListSpotlightChanged(ReadingListSpotlightSample? value) => OnPropertyChanged(nameof(HasReadingListSpotlight));

    [RelayCommand]
    private void OpenCollection(HomeCollectionCard? card)
    {
        if (card is not null)
        {
            _goLibraryWithCollection(card.Id);
        }
    }

    [RelayCommand]
    private void OpenResume(HomeResumeCard? card)
    {
        if (card?.Book is { } book)
        {
            _goReaderForBook(book.BookId, book.Format);
        }
        else if (card?.Comic is { } comic)
        {
            _goReaderForIssue(comic.ResumeIssueId);
        }
    }

    [RelayCommand]
    private void OpenContinueReading(int issueId) => _goReaderForIssue(issueId);

    [RelayCommand]
    private void OpenSeries(SeriesCardSample? card)
    {
        if (card is not null)
        {
            _goDetailForSeries(card.SeriesId);
        }
    }

    [RelayCommand]
    private void OpenReadingListSpotlight()
    {
        if (ReadingListSpotlight is not null)
        {
            _goReaderForIssueInReadingList(ReadingListSpotlight.FirstUnreadIssueId, ReadingListSpotlight.ReadingListId);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // "Not interested" (I5)
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Hides a recommended series from every Because-You-Read row, with an Undo toast. The row edit is deferred a
    /// dispatcher tick: this runs from a button inside the very card being removed (CLAUDE.md, routed-event detach rule).</summary>
    [RelayCommand]
    private void NotInterested(SeriesCardSample? card)
    {
        if (card is null)
        {
            return;
        }

        try
        {
            using var context = PaperbunkrDb.CreateContext();
            DismissedRecommendations.Dismiss(context, card.SeriesId, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Home: dismissing recommendation {card.SeriesId} failed ({ex.GetType().Name}: {ex.Message}).");
            _toastHost?.Show(new ToastRequest("Couldn't hide that recommendation", ex.Message, ToastSeverity.Error));
            return;
        }

        int seriesId = card.SeriesId;
        Dispatcher.UIThread.Post(() => RemoveRecommendation(seriesId));

        _toastHost?.Show(new ToastRequest("Hidden from recommendations", card.Name, ToastSeverity.Info,
            new[] { new ToastAction("Undo", new RelayCommand(() => UndoNotInterested(seriesId))) }));
    }

    private void RemoveRecommendation(int seriesId)
    {
        foreach (var row in BecauseYouRead.ToList())
        {
            foreach (var hit in row.Cards.Where(c => c.SeriesId == seriesId).ToList())
            {
                row.Cards.Remove(hit);
            }

            if (row.Cards.Count == 0)
            {
                BecauseYouRead.Remove(row);
            }
        }

        OnPropertyChanged(nameof(HasBecauseYouRead));
    }

    private void UndoNotInterested(int seriesId)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            DismissedRecommendations.Restore(context, seriesId);
        }

        // Deferred: the Undo button lives inside the toast, which the reload doesn't touch, but a reload is still a bulk
        // collection rebuild best kept out of any routed-event handler.
        Dispatcher.UIThread.Post(LoadFromDatabase);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Search / navigation
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Home's lightweight search entry point - jumps to Library with the query applied to its real, full search.</summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [RelayCommand]
    private void Search()
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            _goLibraryWithSearch(SearchQuery.Trim());
        }
    }

    [RelayCommand]
    private void GoToLibrary() => _goLibraryWithSearch(string.Empty);

    // ---------------------------------------------------------------------------------------------------------------------
    // Loading
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Reloads every visible section from the database, synchronously on the calling thread. Prefer
    /// <see cref="LoadFromDatabaseAsync"/> on any hot UI path.</summary>
    public void LoadFromDatabase() => ApplySnapshot(BuildSnapshot(GetThemeBaseColor()));

    /// <summary>Same as <see cref="LoadFromDatabase"/>, but the queries, card mapping and cover-wall render run on a thread-pool
    /// thread; only the collection repopulation touches the UI thread. Logs and falls back to a synchronous load on failure.</summary>
    public async Task LoadFromDatabaseAsync()
    {
        try
        {
            var themeBaseColor = GetThemeBaseColor(); // UI thread - reads Application.Current.Resources
            var snapshot = await Task.Run(() => BuildSnapshot(themeBaseColor)).ConfigureAwait(true);
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Home async load failed ({ex.GetType().Name}: {ex.Message}) - loading synchronously.");
            LoadFromDatabase();
        }
    }

    /// <summary>Every read for one Home refresh, safe off the UI thread: no observable state touched, no
    /// <c>Application.Current</c> access. Sections the user hid are skipped entirely.</summary>
    private HomeFeedSnapshot BuildSnapshot(SKColor themeBaseColor)
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        var layout = HomeLayout.Resolve(settings.HomeSectionOrder, settings.HomeHiddenSections);
        var visible = layout.Visible.ToHashSet();

        var continueReading = new List<HomeResumeCard>();
        if (visible.Contains(HomeSectionKey.ContinueReading))
        {
            foreach (var candidate in HomeFeedResolver.GetContinueReadingMixed(context))
            {
                if (candidate.Book is { } book)
                {
                    continueReading.Add(new HomeResumeCard { Book = HomeBookCard.FromBook(book) });
                }
                else if (candidate.Series is { } series && candidate.ResumeIssue is { } issue)
                {
                    continueReading.Add(new HomeResumeCard
                    {
                        Comic = new HomeContinueReadingCard
                        {
                            Series = SeriesCardSample.FromSeries(series),
                            ResumeIssueId = issue.Id,
                            ResumeProgressFraction = issue.ReadPercentage() / 100.0,
                            ResumeIssueBadge = string.IsNullOrWhiteSpace(issue.EffectiveNumber()) ? "#?" : $"#{issue.EffectiveNumber()}",
                        },
                    });
                }
            }
        }

        var recentlyAdded = visible.Contains(HomeSectionKey.RecentlyAdded)
            ? HomeFeedResolver.GetRecentlyAdded(context).Select(SeriesCardSample.FromSeries).ToList()
            : new List<SeriesCardSample>();

        var collections = new List<HomeCollectionCard>();
        if (visible.Contains(HomeSectionKey.Collections))
        {
            foreach (var collection in HomeFeedResolver.GetHomeCollections(context))
            {
                var hint = CollectionResolver.GetCoverHint(context, collection.Id);
                var members = hint.ManualPath is null ? CollectionResolver.GetMembers(context, collection.Id) : null;
                collections.Add(HomeCollectionCard.FromCollection(collection, hint, members));
            }
        }

        var becauseYouRead = new List<BecauseYouReadRow>();
        if (visible.Contains(HomeSectionKey.BecauseYouRead))
        {
            var dismissed = DismissedRecommendations.GetIds(context);
            foreach (int seedSeriesId in HomeFeedResolver.GetRecentlyOpenedSeriesIds(context))
            {
                var recommendations = RecommendationResolver.GetRecommendations(context, seedSeriesId)
                    .Where(r => !dismissed.Contains(r.TargetSeriesId))
                    .ToList();
                if (recommendations.Count == 0)
                {
                    continue;
                }

                var seedSeries = context.Series.Include(s => s.Issues).First(s => s.Id == seedSeriesId);
                var targetIds = recommendations.Select(r => r.TargetSeriesId).ToList();
                var targetSeriesById = context.Series
                    .Include(s => s.Issues)
                    .Where(s => targetIds.Contains(s.Id))
                    .ToDictionary(s => s.Id);

                var cards = new ObservableCollection<SeriesCardSample>();
                foreach (var recommendation in recommendations)
                {
                    if (targetSeriesById.TryGetValue(recommendation.TargetSeriesId, out var target))
                    {
                        cards.Add(SeriesCardSample.FromSeries(target));
                    }
                }

                becauseYouRead.Add(new BecauseYouReadRow
                {
                    SeedSeriesName = seedSeries.Name,
                    SeedSeries = SeriesCardSample.FromSeries(seedSeries),
                    Cards = cards,
                });
            }
        }

        var spotlight = visible.Contains(HomeSectionKey.Spotlight)
            ? HomeFeedResolver.GetSpotlightPicks(context, _random, count: SpotlightPickCount).Select(SpotlightIssueSample.FromIssue).ToList()
            : new List<SpotlightIssueSample>();

        HomeAttentionCard? attention = null;
        if (visible.Contains(HomeSectionKey.NeedsAttention))
        {
            try
            {
                if (HomeFeedResolver.GetTopAttention(context, DateTime.UtcNow) is { } pick)
                {
                    var series = context.Series.Include(s => s.Issues).FirstOrDefault(s => s.Id == pick.SeriesId);
                    var sample = series is null ? null : SeriesCardSample.FromSeries(series);
                    attention = new HomeAttentionCard
                    {
                        Attention = pick,
                        CoverBrush = sample?.CoverBrush ?? SeriesCardSample.CoverBrushFor(pick.SeriesName),
                        CoverImage = sample?.CoverKey is string key ? CoverImageCache.Get(key) : null,
                    };
                }
            }
            catch (Exception ex)
            {
                // A failed lookup hides the card, never the whole Home load (spec B, "Error handling").
                DiagnosticsService.LogMilestone($"Home: needs-attention lookup failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        var mastheadBackdrop = BuildMastheadBackdrop(recentlyAdded, becauseYouRead, continueReading, spotlight, themeBaseColor);

        var accentColor = spotlight.Count > 0 && spotlight[0].CoverImage is { } firstCover
            ? SpotlightAccentSampler.Sample(firstCover)
            : SpotlightAccentSampler.FallbackColor;

        ReadingListSpotlightSample? readingListSpotlight = null;
        if (visible.Contains(HomeSectionKey.ReadingList)
            && HomeFeedResolver.GetReadingListSpotlight(context, _random) is { } readingList)
        {
            readingListSpotlight = ReadingListSpotlightSample.FromReadingList(readingList);
        }

        return new HomeFeedSnapshot(layout.Visible, settings.HomeSeasonalFlourish, continueReading, recentlyAdded, collections,
            becauseYouRead, spotlight, attention, mastheadBackdrop, accentColor, readingListSpotlight);
    }

    /// <summary>Pushes a <see cref="BuildSnapshot"/> result into the bound observable state. UI thread.</summary>
    private void ApplySnapshot(HomeFeedSnapshot s)
    {
        _visibleKeys = s.VisibleKeys;
        _seasonalEnabled = s.SeasonalEnabled;

        ReplaceAll(ContinueReading, s.ContinueReading);
        ReplaceAll(RecentlyAdded, s.RecentlyAdded);
        ReplaceAll(Collections, s.Collections);
        ReplaceAll(BecauseYouRead, s.BecauseYouRead);
        ReplaceAll(SpotlightItems, s.Spotlight);
        NeedsAttention = s.Attention;

        ReplaceAll(SpotlightPanels, s.Spotlight.Select(sample => new SpotlightPanelViewModel(sample)).ToList());
        SpotlightIndex = 0;
        OnPropertyChanged(nameof(CurrentSpotlight)); // SetProperty no-ops above if the index was already 0
        SyncOpenPanel();
        // Pre-sampled in BuildSnapshot (index is 0 so CurrentSpotlight == Spotlight[0]).
        SpotlightAccentColor = s.AccentColor;

        MastheadBackdrop = s.MastheadBackdrop;
        ReadingListSpotlight = s.ReadingListSpotlight;

        OnPropertyChanged(nameof(HasContinueReading));
        OnPropertyChanged(nameof(HasRecentlyAdded));
        OnPropertyChanged(nameof(HasBecauseYouRead));
        OnPropertyChanged(nameof(HasSpotlight));
        OnPropertyChanged(nameof(HasCollections));

        RebuildSections();
        UpdateMasthead();

        // Staggered shelf entrance is deliberately NOT triggered here - HomeScreen.axaml.cs flips PlayEntranceAnimation once the
        // view is attached. Refresh() sets it itself (the user is already looking at Home).
    }

    private static void ReplaceAll<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private sealed record HomeFeedSnapshot(
        IReadOnlyList<string> VisibleKeys,
        bool SeasonalEnabled,
        IReadOnlyList<HomeResumeCard> ContinueReading,
        IReadOnlyList<SeriesCardSample> RecentlyAdded,
        IReadOnlyList<HomeCollectionCard> Collections,
        IReadOnlyList<BecauseYouReadRow> BecauseYouRead,
        IReadOnlyList<SpotlightIssueSample> Spotlight,
        HomeAttentionCard? Attention,
        Bitmap? MastheadBackdrop,
        Color AccentColor,
        ReadingListSpotlightSample? ReadingListSpotlight);

    /// <summary>Re-renders the cover wall against the new theme's base colour (no DB requery) and re-evaluates the light-theme
    /// sky, so a theme switch while on Home updates the masthead live.</summary>
    private void OnThemeApplied()
    {
        MastheadBackdrop = BuildMastheadBackdrop(RecentlyAdded, BecauseYouRead, ContinueReading, SpotlightItems, GetThemeBaseColor());
        UpdateMasthead();
    }

    /// <summary>The active theme's <c>surface0</c>, live-updated by <see cref="ThemeService"/> into the application resources.</summary>
    private static SKColor GetThemeBaseColor()
    {
        if (Application.Current is { } app && app.TryGetResource("PbSurface0Color", null, out var resource) && resource is Color color)
        {
            return new SKColor(color.R, color.G, color.B);
        }

        return CoverWallRenderer.DefaultBaseColor;
    }

    /// <summary>Up to 8 cover thumbnails from the just-built rows, composed into the blurred masthead cover wall. Null on a
    /// library with no covers. Static with every input passed in, so <see cref="BuildSnapshot"/> can call it off the UI thread.</summary>
    private static Bitmap? BuildMastheadBackdrop(
        IReadOnlyList<SeriesCardSample> recentlyAdded,
        IReadOnlyList<BecauseYouReadRow> becauseYouRead,
        IReadOnlyList<HomeResumeCard> continueReading,
        IReadOnlyList<SpotlightIssueSample> spotlight,
        SKColor baseColor)
    {
        var covers = new List<Bitmap>();

        void TryAdd(Bitmap? bmp)
        {
            if (bmp is not null && covers.Count < 8 && !covers.Contains(bmp))
            {
                covers.Add(bmp);
            }
        }

        foreach (var card in recentlyAdded)
        {
            TryAdd(card.CoverKey is string key ? CoverImageCache.Get(key) : null);
        }

        foreach (var row in becauseYouRead)
        {
            foreach (var card in row.Cards)
            {
                TryAdd(card.CoverKey is string key ? CoverImageCache.Get(key) : null);
            }
        }

        foreach (var card in continueReading)
        {
            TryAdd(card.CoverImage as Bitmap);
        }

        foreach (var item in spotlight)
        {
            TryAdd(item.CoverImage);
        }

        return covers.Count == 0 ? null : CoverWallRenderer.Render(covers, new PixelSize(1600, 460), baseColor);
    }

    /// <summary>Manual re-query - every section already reloads on every visit, so this is mostly reassurance; still a real
    /// re-roll for the randomized spotlight and reading-list picks.</summary>
    [RelayCommand]
    private void Refresh()
    {
        LoadFromDatabase();
        PlayEntranceAnimation = true;
    }
}
