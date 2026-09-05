using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
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

    public HomeScreenViewModel(Action<int> goDetailForSeries, Action<int> goReaderForIssue, Action<string> goLibraryWithSearch, Action<int, int> goReaderForIssueInReadingList, Action<int, BookFormat> goReaderForBook, Action<int>? goLibraryWithCollection = null)
    {
        _goDetailForSeries = goDetailForSeries;
        _goReaderForIssue = goReaderForIssue;
        _goLibraryWithSearch = goLibraryWithSearch;
        _goReaderForIssueInReadingList = goReaderForIssueInReadingList;
        _goReaderForBook = goReaderForBook;
        _goLibraryWithCollection = goLibraryWithCollection ?? (_ => { });
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

        LoadFromDatabase();
    }

    /// <summary>The spotlight pick adapted to <see cref="IDetailHeaderSource"/> so the Home hero
    /// renders through the shared <c>DetailHero</c> control (docs/superpowers/specs/
    /// 2026-08-28-home-screen-redesign-design.md §3).</summary>
    public HomeSpotlightHeaderSource SpotlightHeader { get; }

    /// <summary>The blurred "cover-wall" behind the masthead - null until the first load, and null
    /// on a library with no covers (the view falls back to a flat gradient).</summary>
    [ObservableProperty]
    private Bitmap? _mastheadBackdrop;

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
    }

    private void AdvanceSpotlight()
    {
        if (SpotlightItems.Count == 0)
        {
            return;
        }

        SpotlightIndex = (SpotlightIndex + 1) % SpotlightItems.Count;
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

    /// <summary>Reloads every module from the database - called once from the constructor and again
    /// every time <c>MainViewModel</c> navigates to Home, matching Library/Smart/Reading's existing
    /// "always fresh, no caching" convention.</summary>
    public void LoadFromDatabase()
    {
        using var context = PaperbunkrDb.CreateContext();

        ContinueReading.Clear();
        foreach (var candidate in HomeFeedResolver.GetContinueReading(context))
        {
            ContinueReading.Add(new HomeContinueReadingCard
            {
                Series = SeriesCardSample.FromSeries(candidate.Series),
                ResumeIssueId = candidate.ResumeIssue.Id,
                ResumeProgressFraction = candidate.ResumeIssue.ReadPercentage() / 100.0,
                ResumeIssueBadge = string.IsNullOrWhiteSpace(candidate.ResumeIssue.EffectiveNumber())
                    ? "#?"
                    : $"#{candidate.ResumeIssue.EffectiveNumber()}",
            });
        }

        HasBooksLibrary = context.Books.Any();
        ContinueReadingBooks.Clear();
        foreach (var book in HomeFeedResolver.GetContinueReadingBooks(context))
        {
            ContinueReadingBooks.Add(HomeBookCard.FromBook(book));
        }

        RecentlyAdded.Clear();
        foreach (var series in HomeFeedResolver.GetRecentlyAdded(context))
        {
            RecentlyAdded.Add(SeriesCardSample.FromSeries(series));
        }

        Collections.Clear();
        foreach (var collection in HomeFeedResolver.GetHomeCollections(context))
        {
            var hint = CollectionResolver.GetCoverHint(context, collection.Id);
            Collections.Add(HomeCollectionCard.FromCollection(collection, hint));
        }

        BecauseYouRead.Clear();
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

            BecauseYouRead.Add(new BecauseYouReadRow { SeedSeriesName = seedSeries.Name, Cards = cards });
        }

        SpotlightItems.Clear();
        foreach (var issue in HomeFeedResolver.GetSpotlightPicks(context, _random))
        {
            SpotlightItems.Add(SpotlightIssueSample.FromIssue(issue));
        }

        SpotlightIndex = 0;
        OnPropertyChanged(nameof(CurrentSpotlight)); // SetProperty no-ops above if the index was already 0
        SpotlightHeader.RaiseChanged();

        MastheadBackdrop = BuildMastheadBackdrop();

        var spotlightList = HomeFeedResolver.GetReadingListSpotlight(context, _random);
        ReadingListSpotlight = spotlightList is null ? null : ReadingListSpotlightSample.FromReadingList(spotlightList);

        OnPropertyChanged(nameof(HasContinueReading));
        OnPropertyChanged(nameof(HasContinueReadingBooks));
        OnPropertyChanged(nameof(HasRecentlyAdded));
        OnPropertyChanged(nameof(HasBecauseYouRead));
        OnPropertyChanged(nameof(HasSpotlight));
        OnPropertyChanged(nameof(HasCollections));
    }

    partial void OnReadingListSpotlightChanged(ReadingListSpotlightSample? value) => OnPropertyChanged(nameof(HasReadingListSpotlight));

    /// <summary>Gathers up to 8 cover thumbnails already loaded for this visit (Recently Added,
    /// Because You Read, Continue Reading, Spotlight) and composes the blurred masthead cover-wall
    /// (docs/superpowers/specs/2026-08-28-home-screen-redesign-design.md §2). Returns null on a
    /// library with no covers - the view then shows a flat gradient.</summary>
    private Bitmap? BuildMastheadBackdrop()
    {
        var covers = new List<Bitmap>();

        void TryAdd(Bitmap? bmp)
        {
            if (bmp is not null && covers.Count < 8 && !covers.Contains(bmp))
            {
                covers.Add(bmp);
            }
        }

        foreach (var card in RecentlyAdded)
        {
            TryAdd(card.CoverKey is string key ? CoverImageCache.Get(key) : null);
        }

        foreach (var row in BecauseYouRead)
        {
            foreach (var card in row.Cards)
            {
                TryAdd(card.CoverKey is string key ? CoverImageCache.Get(key) : null);
            }
        }

        foreach (var card in ContinueReading)
        {
            TryAdd(card.Series.CoverKey is string key ? CoverImageCache.Get(key) : null);
        }

        foreach (var item in SpotlightItems)
        {
            TryAdd(item.CoverImage);
        }

        return covers.Count == 0 ? null : CoverWallRenderer.Render(covers, new PixelSize(1600, 460));
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
    private void Refresh() => LoadFromDatabase();
}
