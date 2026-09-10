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
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using SkiaSharp;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// App launch screen (docs/superpowers/specs/2026-08-18-home-screen-design.md) - five modules, all
/// re-queried fresh on every visit, no caching, matching every other rail-nav screen's existing
/// convention. Query/pick logic itself lives in <see cref="HomeFeedResolver"/> (and, for Module 3,
/// the already-shipped <see cref="RecommendationResolver"/>) - this ViewModel only maps their
/// results into display cards and wires navigation.
/// </summary>
public partial class HomeScreenViewModel : ViewModelBase
{
    /// <summary>Spotlight carousel auto-advance cadence - matches the companion project's own value
    /// this was ported from (7 seconds), same tradeoff category as <see cref="ReaderScreenViewModel"/>'s
    /// own timer intervals (long enough to actually read a card before it moves on).</summary>
    private static readonly TimeSpan SpotlightAdvanceInterval = TimeSpan.FromSeconds(7);

    private readonly Action<int> _goDetailForSeries;
    private readonly Action<int> _goReaderForIssue;
    private readonly Action<string> _goLibraryWithSearch;
    private readonly Action<int, int> _goReaderForIssueInReadingList;
    private readonly Action<int, BookFormat> _goReaderForBook;
    private readonly Action<int> _goLibraryWithCollection;
    private readonly Random _random = new();
    private readonly DispatcherTimer _spotlightTimer;
    private readonly SkinService _skinService;

    public HomeScreenViewModel(Action<int> goDetailForSeries, Action<int> goReaderForIssue, Action<string> goLibraryWithSearch, Action<int, int> goReaderForIssueInReadingList, Action<int, BookFormat> goReaderForBook, Action<int>? goLibraryWithCollection = null, SkinService? skinService = null, bool loadOnConstruction = true)
    {
        _goDetailForSeries = goDetailForSeries;
        _goReaderForIssue = goReaderForIssue;
        _goLibraryWithSearch = goLibraryWithSearch;
        _goReaderForIssueInReadingList = goReaderForIssueInReadingList;
        _goReaderForBook = goReaderForBook;
        _goLibraryWithCollection = goLibraryWithCollection ?? (_ => { });
        // Defaults to a standalone instance for callers that don't pass one (matches this
        // constructor's other optional-parameter defaults) - production always passes MainViewModel's
        // shared instance so the masthead's SkinApplied subscription (see ctor body below) actually
        // fires when the user switches skins via Preferences.
        _skinService = skinService ?? new SkinService();
        _skinService.SkinApplied += OnSkinApplied;
        ContinueReading = new ObservableCollection<HomeContinueReadingCard>();
        ContinueReadingBooks = new ObservableCollection<HomeBookCard>();
        RecentlyAdded = new ObservableCollection<SeriesCardSample>();
        BecauseYouRead = new ObservableCollection<BecauseYouReadRow>();
        SpotlightItems = new ObservableCollection<SpotlightIssueSample>();
        Collections = new ObservableCollection<HomeCollectionCard>();

        // No IDisposable on this ViewModel (same as ReaderScreenViewModel's own timers) - Home is a
        // singleton created once in MainViewModel's constructor and lives for the app's lifetime, so
        // there's nothing to clean up. Ticking while Home isn't the visible screen is harmless - it
        // only advances an index no one's looking at.
        _spotlightTimer = new DispatcherTimer { Interval = SpotlightAdvanceInterval };
        _spotlightTimer.Tick += (_, _) => AdvanceSpotlight();
        _spotlightTimer.Start();

        SpotlightHeader = new HomeSpotlightHeaderSource(() => CurrentSpotlight, OpenSpotlightCommand);

        // Production (MainViewModel) passes loadOnConstruction: false. Home is only ever shown via
        // GoHome() (or the two explicit Home.LoadFromDatabase()+CurrentScreen="home" pairs in
        // MainViewModel), each of which loads first - so loading here too was pure duplicate work,
        // and it ran on the UI thread during startup while the splash was still up (~2.7s of the
        // frozen-splash window: five feed queries + a SkiaSharp cover-wall render). Tests default
        // to true for construct-then-assert convenience.
        if (loadOnConstruction)
        {
            LoadFromDatabase();
        }
    }

    /// <summary>The spotlight pick adapted to <see cref="IDetailHeaderSource"/> so the Home hero
    /// renders through the shared <c>DetailHero</c> control (docs/superpowers/specs/
    /// 2026-08-28-home-screen-redesign-design.md §3).</summary>
    public HomeSpotlightHeaderSource SpotlightHeader { get; }

    /// <summary>The blurred "cover-wall" behind the masthead - null until the first load, and null
    /// on a library with no covers (the view falls back to a flat gradient).</summary>
    [ObservableProperty]
    private Bitmap? _mastheadBackdrop;

    /// <summary>Average color of the current spotlight's cover (docs/superpowers/specs/
    /// 2026-09-08-home-navrail-visual-v2-design.md §4), sampled via <see cref="SpotlightAccentSampler"/>
    /// - the view blends this into the masthead scrim so it visibly reacts to whichever issue is
    /// currently featured. <see cref="SpotlightAccentSampler.FallbackColor"/> when there's no
    /// spotlight cover to sample.</summary>
    [ObservableProperty]
    private Color _spotlightAccentColor = SpotlightAccentSampler.FallbackColor;

    /// <summary>Staggered shelf-card entrance (docs/superpowers/specs/2026-09-08-home-navrail-
    /// visual-v2-design.md §6), same one-shot-per-trigger contract as
    /// <see cref="LibraryScreenViewModel.PlayEntranceAnimation"/>: read once per container
    /// preparation by <see cref="Controls.EntranceAnimation.Prepare"/>, not a live binding, so
    /// ordinary scroll-driven virtualization recycling never replays it - only
    /// <see cref="LoadFromDatabase"/> setting this back to true does.</summary>
    [ObservableProperty]
    private bool _playEntranceAnimation;

    public ObservableCollection<HomeContinueReadingCard> ContinueReading { get; }

    /// <summary>Novels the user is mid-way through (docs/superpowers/specs/2026-08-27-books-screen-
    /// chrome-and-home-strip-design.md) - a separate row from <see cref="ContinueReading"/>, shown
    /// only when the library actually has books (<see cref="HasBooksLibrary"/>).</summary>
    public ObservableCollection<HomeBookCard> ContinueReadingBooks { get; }

    public ObservableCollection<SeriesCardSample> RecentlyAdded { get; }
    public ObservableCollection<BecauseYouReadRow> BecauseYouRead { get; }

    /// <summary>Spotlight carousel's full pick set (docs/superpowers/specs/2026-08-18-home-screen-
    /// design.md) - <see cref="SpotlightIndex"/> selects which one <see cref="CurrentSpotlight"/>
    /// currently shows; a <see cref="DispatcherTimer"/> advances it automatically, dots let a click
    /// jump straight to one.</summary>
    public ObservableCollection<SpotlightIssueSample> SpotlightItems { get; }

    /// <summary>"Collections" shelf (docs/superpowers/specs/2026-08-27-collections-design.md's own
    /// deferred "Home-feed shelf" follow-on) - non-empty collections, capped, in the user's own
    /// sidebar order.</summary>
    public ObservableCollection<HomeCollectionCard> Collections { get; }

    public bool HasCollections => Collections.Count > 0;

    [RelayCommand]
    private void OpenCollection(HomeCollectionCard? card)
    {
        if (card is not null)
        {
            _goLibraryWithCollection(card.Id);
        }
    }

    [ObservableProperty]
    private int _spotlightIndex;

    public SpotlightIssueSample? CurrentSpotlight =>
        SpotlightIndex >= 0 && SpotlightIndex < SpotlightItems.Count ? SpotlightItems[SpotlightIndex] : null;

    partial void OnSpotlightIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentSpotlight));
        SpotlightHeader.RaiseChanged();
        UpdateSpotlightAccentColor();
    }

    /// <summary>Re-samples <see cref="SpotlightAccentColor"/> from <see cref="CurrentSpotlight"/>'s
    /// cover (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md §4) - called on
    /// every spotlight rotation/click and once after <see cref="LoadFromDatabase"/> repopulates
    /// <see cref="SpotlightItems"/>.</summary>
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

    /// <summary>Hovering the hero card or a dot pauses auto-rotation so the reader isn't fighting
    /// the carousel to read it (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md
    /// §5); <see cref="ResumeSpotlightRotation"/> on pointer-leave restarts the normal cadence.</summary>
    public void PauseSpotlightRotation() => _spotlightTimer.Stop();

    public void ResumeSpotlightRotation() => _spotlightTimer.Start();

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

    [ObservableProperty]
    private ReadingListSpotlightSample? _readingListSpotlight;

    /// <summary>Home's own lightweight search entry point - jumps straight to Library with the query
    /// already applied to its real, full CE-parity search, rather than a second, more limited search
    /// experience living here too.</summary>
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

    /// <summary>"View Library" link (Module 2's header) - reuses the same callback <see cref="Search"/>
    /// does, just with no query, matching <see cref="LibraryScreenViewModel"/>'s own "empty
    /// SearchQuery means no filter" behavior exactly.</summary>
    [RelayCommand]
    private void GoToLibrary() => _goLibraryWithSearch(string.Empty);

    public bool HasContinueReading => ContinueReading.Count > 0;

    /// <summary>Whether the library contains any book at all - gates the whole "Continue Reading —
    /// Books" row (header included) so books stay invisible to comic-only users.</summary>
    [ObservableProperty]
    private bool _hasBooksLibrary;

    public bool HasContinueReadingBooks => ContinueReadingBooks.Count > 0;

    public bool HasRecentlyAdded => RecentlyAdded.Count > 0;
    public bool HasBecauseYouRead => BecauseYouRead.Count > 0;
    public bool HasSpotlight => SpotlightItems.Count > 0;
    public bool HasReadingListSpotlight => ReadingListSpotlight is not null;

    /// <summary>Reloads every module from the database - called once from the constructor (unless
    /// <c>loadOnConstruction:false</c>) and again every time <c>MainViewModel</c> navigates to Home,
    /// matching Library/Smart/Reading's "always fresh, no caching" convention. Synchronous: the
    /// queries and the 1600x460 cover-wall render run on the calling thread. Prefer
    /// <see cref="LoadFromDatabaseAsync"/> off any hot UI path (it's what the startup Home load
    /// uses - a synchronous load there visibly froze the just-shown shell for ~2.7s).</summary>
    public void LoadFromDatabase() => ApplySnapshot(BuildSnapshot(GetSkinBaseColor()));

    /// <summary>Same as <see cref="LoadFromDatabase"/>, but the DB queries, sample mapping and the
    /// cover-wall render happen on a thread-pool thread; only the observable-collection
    /// repopulation touches the UI thread. Fire-and-forget safe - it logs and falls back to a
    /// synchronous load rather than faulting an unobserved task.</summary>
    public async Task LoadFromDatabaseAsync()
    {
        try
        {
            var skinBaseColor = GetSkinBaseColor(); // UI thread - reads Application.Current.Resources
            var snapshot = await Task.Run(() => BuildSnapshot(skinBaseColor)).ConfigureAwait(true);
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Home async load failed ({ex.GetType().Name}: {ex.Message}) - loading synchronously.");
            LoadFromDatabase();
        }
    }

    /// <summary>Every read for one Home refresh, built off any thread: no <c>this</c> observable
    /// state is touched and there is no <c>Application.Current</c> access (the skin colour is
    /// passed in). <see cref="CoverImageCache"/> is internally locked, so the cover-wall render is
    /// safe here.</summary>
    private HomeFeedSnapshot BuildSnapshot(SKColor skinBaseColor)
    {
        using var context = PaperbunkrDb.CreateContext();

        var continueReading = new List<HomeContinueReadingCard>();
        foreach (var candidate in HomeFeedResolver.GetContinueReading(context))
        {
            continueReading.Add(new HomeContinueReadingCard
            {
                Series = SeriesCardSample.FromSeries(candidate.Series),
                ResumeIssueId = candidate.ResumeIssue.Id,
                ResumeProgressFraction = candidate.ResumeIssue.ReadPercentage() / 100.0,
                ResumeIssueBadge = string.IsNullOrWhiteSpace(candidate.ResumeIssue.EffectiveNumber())
                    ? "#?"
                    : $"#{candidate.ResumeIssue.EffectiveNumber()}",
            });
        }

        bool hasBooksLibrary = context.Books.Any();
        var continueReadingBooks = HomeFeedResolver.GetContinueReadingBooks(context)
            .Select(HomeBookCard.FromBook).ToList();
        var recentlyAdded = HomeFeedResolver.GetRecentlyAdded(context)
            .Select(SeriesCardSample.FromSeries).ToList();

        var collections = new List<HomeCollectionCard>();
        foreach (var collection in HomeFeedResolver.GetHomeCollections(context))
        {
            var hint = CollectionResolver.GetCoverHint(context, collection.Id);
            collections.Add(HomeCollectionCard.FromCollection(collection, hint));
        }

        var becauseYouRead = new List<BecauseYouReadRow>();
        foreach (int seedSeriesId in HomeFeedResolver.GetRecentlyOpenedSeriesIds(context))
        {
            var recommendations = RecommendationResolver.GetRecommendations(context, seedSeriesId);
            if (recommendations.Count == 0)
            {
                continue;
            }

            var seedSeries = context.Series.First(s => s.Id == seedSeriesId);
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

            becauseYouRead.Add(new BecauseYouReadRow { SeedSeriesName = seedSeries.Name, Cards = cards });
        }

        var spotlight = HomeFeedResolver.GetSpotlightPicks(context, _random)
            .Select(SpotlightIssueSample.FromIssue).ToList();

        var mastheadBackdrop = BuildMastheadBackdrop(recentlyAdded, becauseYouRead, continueReading, spotlight, skinBaseColor);

        var accentColor = spotlight.Count > 0 && spotlight[0].CoverImage is { } firstCover
            ? SpotlightAccentSampler.Sample(firstCover)
            : SpotlightAccentSampler.FallbackColor;

        var readingList = HomeFeedResolver.GetReadingListSpotlight(context, _random);
        var readingListSpotlight = readingList is null ? null : ReadingListSpotlightSample.FromReadingList(readingList);

        return new HomeFeedSnapshot(continueReading, hasBooksLibrary, continueReadingBooks, recentlyAdded,
            collections, becauseYouRead, spotlight, mastheadBackdrop, accentColor, readingListSpotlight);
    }

    /// <summary>Pushes a <see cref="BuildSnapshot"/> result into the bound observable state. UI thread.</summary>
    private void ApplySnapshot(HomeFeedSnapshot s)
    {
        ReplaceAll(ContinueReading, s.ContinueReading);
        HasBooksLibrary = s.HasBooksLibrary;
        ReplaceAll(ContinueReadingBooks, s.ContinueReadingBooks);
        ReplaceAll(RecentlyAdded, s.RecentlyAdded);
        ReplaceAll(Collections, s.Collections);
        ReplaceAll(BecauseYouRead, s.BecauseYouRead);
        ReplaceAll(SpotlightItems, s.Spotlight);

        SpotlightIndex = 0;
        OnPropertyChanged(nameof(CurrentSpotlight)); // SetProperty no-ops above if the index was already 0
        SpotlightHeader.RaiseChanged();
        // Pre-sampled in BuildSnapshot: index is 0 so CurrentSpotlight == Spotlight[0]. Set directly
        // (OnSpotlightIndexChanged's UpdateSpotlightAccentColor() call no-ops when index was already 0).
        SpotlightAccentColor = s.AccentColor;

        MastheadBackdrop = s.MastheadBackdrop;

        ReadingListSpotlight = s.ReadingListSpotlight;

        OnPropertyChanged(nameof(HasContinueReading));
        OnPropertyChanged(nameof(HasContinueReadingBooks));
        OnPropertyChanged(nameof(HasRecentlyAdded));
        OnPropertyChanged(nameof(HasBecauseYouRead));
        OnPropertyChanged(nameof(HasSpotlight));
        OnPropertyChanged(nameof(HasCollections));

        // Staggered shelf entrance is deliberately NOT triggered here (docs/superpowers/specs/
        // 2026-09-08-home-navrail-visual-v2-design.md §6) - HomeScreen.axaml.cs flips
        // PlayEntranceAnimation once the View is attached and EntranceAnimation's container-prepared
        // subscription is live. With LoadFromDatabaseAsync that ordering actually improves: the View
        // attaches to an empty list, sets the flag, then the items arrive and stagger in. Refresh()
        // still sets the flag itself (the user is already looking at Home).
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
        IReadOnlyList<HomeContinueReadingCard> ContinueReading,
        bool HasBooksLibrary,
        IReadOnlyList<HomeBookCard> ContinueReadingBooks,
        IReadOnlyList<SeriesCardSample> RecentlyAdded,
        IReadOnlyList<HomeCollectionCard> Collections,
        IReadOnlyList<BecauseYouReadRow> BecauseYouRead,
        IReadOnlyList<SpotlightIssueSample> Spotlight,
        Bitmap? MastheadBackdrop,
        Color AccentColor,
        ReadingListSpotlightSample? ReadingListSpotlight);

    partial void OnReadingListSpotlightChanged(ReadingListSpotlightSample? value) => OnPropertyChanged(nameof(HasReadingListSpotlight));

    /// <summary>
    /// Re-renders the masthead cover-wall against the newly-active skin's colors, without a DB
    /// requery - <see cref="BuildMastheadBackdrop"/> only reads already-loaded in-memory cover
    /// collections (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md §3), so
    /// switching skins while already on Home updates the masthead live instead of requiring a
    /// navigate-away/back.
    /// </summary>
    private void OnSkinApplied() => MastheadBackdrop = BuildMastheadBackdrop(
        RecentlyAdded, BecauseYouRead, ContinueReading, SpotlightItems, GetSkinBaseColor());

    /// <summary>The active skin's <c>surface0</c>, live-updated by <see cref="SkinService"/> into
    /// <c>Application.Current.Resources</c> - read directly rather than adding a new
    /// <see cref="SkinService"/> accessor (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-
    /// design.md §3's own open question). Falls back to <see cref="CoverWallRenderer.DefaultBaseColor"/>
    /// if the resource somehow isn't registered yet (e.g. a design-time/test host with no App.axaml
    /// resources loaded).</summary>
    private static SKColor GetSkinBaseColor()
    {
        if (Application.Current is { } app && app.TryGetResource("PbSurface0Color", null, out var resource) && resource is Color color)
        {
            return new SKColor(color.R, color.G, color.B);
        }

        return CoverWallRenderer.DefaultBaseColor;
    }

    /// <summary>Gathers up to 8 cover thumbnails from the just-built feed rows (Recently Added,
    /// Because You Read, Continue Reading, Spotlight) and composes the blurred masthead cover-wall
    /// (docs/superpowers/specs/2026-08-28-home-screen-redesign-design.md §2). Returns null on a
    /// library with no covers - the view then shows a flat gradient. <c>static</c> + all inputs
    /// passed in (no <c>this</c>, no <c>Application.Current</c>) so <see cref="BuildSnapshot"/> can
    /// call it off the UI thread; <see cref="CoverImageCache"/> is internally locked.</summary>
    private static Bitmap? BuildMastheadBackdrop(
        IReadOnlyList<SeriesCardSample> recentlyAdded,
        IReadOnlyList<BecauseYouReadRow> becauseYouRead,
        IReadOnlyList<HomeContinueReadingCard> continueReading,
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
            TryAdd(card.Series.CoverKey is string key ? CoverImageCache.Get(key) : null);
        }

        foreach (var item in spotlight)
        {
            TryAdd(item.CoverImage);
        }

        return covers.Count == 0 ? null : CoverWallRenderer.Render(covers, new PixelSize(1600, 460), baseColor);
    }

    [RelayCommand]
    private void OpenContinueReading(int issueId) => _goReaderForIssue(issueId);

    [RelayCommand]
    private void OpenContinueReadingBook(HomeBookCard? card)
    {
        if (card is not null)
        {
            _goReaderForBook(card.BookId, card.Format);
        }
    }

    [RelayCommand]
    private void OpenSeries(SeriesCardSample? card)
    {
        if (card is not null)
        {
            _goDetailForSeries(card.SeriesId);
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

    [RelayCommand]
    private void OpenReadingListSpotlight()
    {
        if (ReadingListSpotlight is not null)
        {
            _goReaderForIssueInReadingList(ReadingListSpotlight.FirstUnreadIssueId, ReadingListSpotlight.ReadingListId);
        }
    }

    /// <summary>Manual re-query - every module already reloads fresh on every visit to Home
    /// (<see cref="LoadFromDatabase"/>'s own doc comment), so this is mostly reassurance rather than
    /// something strictly needed; still a real re-roll for the two randomized single-pick modules.</summary>
    [RelayCommand]
    private void Refresh()
    {
        LoadFromDatabase();

        // Safe here (unlike inside LoadFromDatabase itself - see that method's own comment on this
        // exact point): the user had to already be looking at Home to click Refresh, so the View is
        // definitely attached and EntranceAnimation's subscription is definitely live.
        PlayEntranceAnimation = true;
    }
}
