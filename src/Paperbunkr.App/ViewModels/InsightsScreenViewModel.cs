using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Insights screen (docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md §5, revised
/// after the first on-screen pass to keep Stats a *tab* on this screen rather than a separate
/// nav-rail destination - matches this project's established "discrete section switching over
/// scattering related content across destinations" preference; tabs renamed Overview/Stats -> Today/
/// Trends per docs/superpowers/specs/2026-09-23-insights-redesign-design.md). Two tabs sharing one
/// nav-rail entry: **Today** (READING attention cards + Collection health - unchanged) and **Trends**
/// (the MangaBaka-style analytics, hosted by <see cref="Stats"/>), plus **History** (Mihon-style reading
/// history, <see cref="History"/> - docs/superpowers/specs/2026-09-29-insights-reading-history-design.md)
/// and the seasonal **Recap** (<see cref="Recap"/>). All computation is in
/// <see cref="InsightsResolver"/>/<see cref="StatsResolver"/>; this class is presentation glue + the
/// session cache for the Today tab (Stats owns its own cache).
/// </summary>
public partial class InsightsScreenViewModel : ViewModelBase
{
    private readonly Action<int> _goReaderForIssue;
    private readonly Action<int> _goDetailForSeries;
    private readonly Func<DateTime> _nowUtc;
    private InsightsSnapshot? _cache;

    private readonly Action _openNewGoalDialog;

    public InsightsScreenViewModel(
        Action<int> goReaderForIssue,
        Action<int> goDetailForSeries,
        Action<string> goLibraryWithSearch,
        Action openNewGoalDialog,
        IDialogService dialogs,
        IReadingEventRecorder? readingEventRecorder = null,
        Func<DateTime>? nowUtc = null,
        IActivityService? activity = null,
        Action<int, BookFormat>? goReaderForBook = null,
        Action<int>? goBookDetailForBook = null,
        HistoryTabViewModel? history = null,
        InsightsPreferences? preferences = null,
        Action? openDuplicateReview = null)
    {
        _goReaderForIssue = goReaderForIssue;
        _goDetailForSeries = goDetailForSeries;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        _openNewGoalDialog = openNewGoalDialog;
        Activity = activity;
        Stats = new StatsScreenViewModel(goLibraryWithSearch, readingEventRecorder, nowUtc);
        Recap = new RecapViewModel(readingEventRecorder, nowUtc);
        Goals = new GoalsViewModel(dialogs, readingEventRecorder, nowUtc) { Activity = activity };
        History = history ?? new HistoryTabViewModel(
            goReaderForIssue, goDetailForSeries, goReaderForBook ?? ((_, _) => { }), goBookDetailForBook ?? (_ => { }),
            dialogs, readingEventRecorder, activity, nowUtc: nowUtc);
        History.Preferences = preferences;
        Stats.OpenDuplicateReview = openDuplicateReview;
        Stats.GoDetailForSeries = goDetailForSeries;
        Stats.Activity = activity;

        if (readingEventRecorder is not null)
        {
            readingEventRecorder.ReadingEventRecorded += () =>
            {
                _cache = null;
                _refreshGeneration++; // a load already running was built from the old events
                // Cheap: only recompute if the screen is the one on show. The shell calls Refresh()
                // on navigation anyway, so a stale cache while elsewhere is harmless.
                if (IsActive)
                {
                    Refresh();
                }
            };
        }
    }

    /// <summary>Set by the shell while the Insights screen is the visible lateral screen.</summary>
    public bool IsActive { get; set; }

    /// <summary>Used only by the Recap tab's export flow to report a render/save failure
    /// (docs/superpowers/specs/2026-09-23-insights-year-in-review-recap-design.md's Error handling
    /// section) - null in tests, where export isn't exercised.</summary>
    public IActivityService? Activity { get; }

    /// <summary>The Stats tab's own view-model - separate cache/range from the Overview tab above,
    /// since most of its tiles are range-aware and none of Overview's are.</summary>
    public StatsScreenViewModel Stats { get; }

    /// <summary>The Recap tab's own view-model - separate cache/year from Overview and Stats
    /// (docs/superpowers/specs/2026-09-23-insights-year-in-review-recap-design.md).</summary>
    public RecapViewModel Recap { get; }

    /// <summary>Reading-goal cards on the Overview tab (docs/superpowers/specs/2026-09-23-insights-
    /// reading-goals-design.md) - unlike <see cref="Stats"/>/<see cref="Recap"/>, refreshed unconditionally
    /// alongside Overview's own content rather than only while a specific tab is selected, since the goal
    /// cards live on Overview itself.</summary>
    public GoalsViewModel Goals { get; }

    /// <summary>The History tab's own view-model (docs/superpowers/specs/2026-09-29-insights-reading-history-
    /// design.md §3) - its own cache, loaded only while the tab is selected.</summary>
    public HistoryTabViewModel History { get; }

    [RelayCommand]
    private void AddGoal() => _openNewGoalDialog();

    [ObservableProperty]
    private bool _isTrendsTabSelected;

    [ObservableProperty]
    private bool _isRecapTabSelected;

    [ObservableProperty]
    private bool _isHistoryTabSelected;

    partial void OnIsTrendsTabSelectedChanged(bool value)
    {
        if (value)
        {
            IsRecapTabSelected = false;
            IsHistoryTabSelected = false;
        }

        Stats.IsActive = value;
        if (value)
        {
            Stats.Refresh();
        }

        OnPropertyChanged(nameof(IsTodayTabSelected));
    }

    partial void OnIsRecapTabSelectedChanged(bool value)
    {
        if (value)
        {
            IsTrendsTabSelected = false;
            IsHistoryTabSelected = false;
        }

        Recap.IsActive = value;
        if (value)
        {
            Recap.Refresh();
        }

        OnPropertyChanged(nameof(IsTodayTabSelected));
    }

    partial void OnIsHistoryTabSelectedChanged(bool value)
    {
        if (value)
        {
            IsTrendsTabSelected = false;
            IsRecapTabSelected = false;
        }

        History.IsActive = value;
        if (value)
        {
            History.Refresh();
        }

        OnPropertyChanged(nameof(IsTodayTabSelected));
    }

    /// <summary>True when none of Trends / History / Recap is selected - the implicit default state
    /// of what used to be a plain <c>!IsTrendsTabSelected</c> 2-way toggle.</summary>
    public bool IsTodayTabSelected => !IsTrendsTabSelected && !IsHistoryTabSelected && !IsRecapTabSelected;

    [RelayCommand]
    private void SelectTodayTab()
    {
        IsTrendsTabSelected = false;
        IsHistoryTabSelected = false;
        IsRecapTabSelected = false;
        _pluginWidgets = null;   // rebuilt on the way in, so a plugin's card shows current numbers
        OnPropertyChanged(nameof(PluginWidgets));
        OnPropertyChanged(nameof(HasPluginWidgets));
    }

    private IReadOnlyList<Paperbunkr.App.Plugins.PluginView>? _pluginWidgets;

    /// <summary>Native plugins' Insights cards (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §E), shown at the bottom of Today.</summary>
    public IReadOnlyList<Paperbunkr.App.Plugins.PluginView> PluginWidgets =>
        _pluginWidgets ??= Paperbunkr.App.Plugins.PluginHostService.Current?.GetInsightsWidgets() ?? Array.Empty<Paperbunkr.App.Plugins.PluginView>();

    public bool HasPluginWidgets => PluginWidgets.Count > 0;

    [RelayCommand]
    private void SelectTrendsTab() => IsTrendsTabSelected = true;

    [RelayCommand]
    private void SelectHistoryTab() => IsHistoryTabSelected = true;

    [RelayCommand]
    private void SelectRecapTab()
    {
        if (IsRecapAvailable)
        {
            IsRecapTabSelected = true;
        }
    }

    /// <summary>Recap is a once-a-year "Wrapped"-style moment, not a daily-use tab - visible only in the
    /// last 3 calendar days of the year (Dec 29-31, local time), hidden the rest of the year. The tab
    /// button itself is hidden when this is false (see <c>InsightsScreen.axaml</c>), and
    /// <see cref="SelectRecapTab"/> also guards entry directly in case something else ever calls it.</summary>
    public bool IsRecapAvailable => IsWithinYearEndWindow(_nowUtc());

    internal static bool IsWithinYearEndWindow(DateTime nowUtc)
    {
        var local = nowUtc.ToLocalTime();
        return local.Month == 12 && local.Day >= 29;
    }

    [ObservableProperty]
    private InsightsSnapshot? _snapshot;

    /// <summary>The "Because you finished X" section on the Overview tab (docs/superpowers/specs/2026-09-23-
    /// insights-recommendations-surface-design.md) - null seed name / empty collection hides the whole
    /// section, no empty-state placeholder.</summary>
    [ObservableProperty]
    private string? _recommendationsSeedName;

    public ObservableCollection<InsightsRecommendationCard> Recommendations { get; } = new();

    public bool HasRecommendations => Recommendations.Count > 0;

    /// <summary>The Today hero row's single recommendation pick (docs/superpowers/specs/2026-09-23-
    /// insights-redesign-design.md). The rest move to <see cref="SecondaryRecommendations"/>, a compact
    /// list below the hero row.</summary>
    public InsightsRecommendationCard? PrimaryRecommendation => Recommendations.Count > 0 ? Recommendations[0] : null;

    public IReadOnlyList<InsightsRecommendationCard> SecondaryRecommendations => Recommendations.Skip(1).ToList();

    public bool HasSecondaryRecommendations => SecondaryRecommendations.Count > 0;

    public ObservableCollection<AttentionRow> ContinueRows { get; } = new();
    public ObservableCollection<AttentionRow> AlmostDoneRows { get; } = new();
    public ObservableCollection<AttentionRow> DiveInRows { get; } = new();
    public ObservableCollection<GapRow> GapRows { get; } = new();

    public int ContinueCount => Snapshot?.Continue.Count ?? 0;
    public int AlmostDoneCount => Snapshot?.AlmostDone.Count ?? 0;
    public int DiveInCount => Snapshot?.DiveIn.Count ?? 0;
    public int GapCount => Snapshot?.Gaps.Count ?? 0;

    public bool ContinueEmpty => ContinueCount == 0;
    public bool AlmostDoneEmpty => AlmostDoneCount == 0;
    public bool DiveInEmpty => DiveInCount == 0;
    public bool GapEmpty => GapCount == 0;

    /// <summary>Nothing to read-next in any of the three "Reading" cards (gaps live in Collection health, not here).</summary>
    public bool ReadingAllClear => ContinueEmpty && AlmostDoneEmpty && DiveInEmpty;

    /// <summary>Nothing left in the "READING" section specifically - Continue moved into the hero row
    /// (docs/superpowers/specs/2026-09-23-insights-redesign-design.md), so this section's own all-clear
    /// state no longer depends on it the way <see cref="ReadingAllClear"/> still does.</summary>
    public bool ReadingSectionEmpty => AlmostDoneEmpty && DiveInEmpty;

    /// <summary>The Today hero row's continue-reading pick.</summary>
    public AttentionRow? PrimaryContinueRow => ContinueRows.Count > 0 ? ContinueRows[0] : null;

    // --- The Today spotlight (docs/superpowers/specs/2026-10-20-insights-today-redesign-design.md): the series being continued, or - only when there
    // is nothing to continue - the top recommendation. ---

    public bool HasSpotlight => PrimaryContinueRow is not null || PrimaryRecommendation is not null;

    public bool SpotlightIsContinue => PrimaryContinueRow is not null;

    public string SpotlightHeading => PrimaryContinueRow is not null
        ? "CONTINUE READING"
        : string.IsNullOrWhiteSpace(RecommendationsSeedName) ? "RECOMMENDED FOR YOU" : $"BECAUSE YOU FINISHED {RecommendationsSeedName}".ToUpperInvariant();

    public string SpotlightTitle => PrimaryContinueRow?.Title ?? PrimaryRecommendation?.Card.Name ?? string.Empty;

    public string SpotlightSubtitle => PrimaryContinueRow?.Subtitle ?? PrimaryRecommendation?.Explanation ?? string.Empty;

    public string? SpotlightCoverKey => PrimaryContinueRow?.CoverKey ?? PrimaryRecommendation?.Card.CoverKey;

    public string SpotlightActionLabel => PrimaryContinueRow is not null ? "Continue reading" : "View series";

    [RelayCommand]
    private void OpenSpotlight()
    {
        if (PrimaryContinueRow is { } row)
        {
            OpenContinue(row);
        }
        else if (PrimaryRecommendation is { } recommendation)
        {
            OpenSeries(recommendation.Card.SeriesId);
        }
    }

    /// <summary>True once the hero row has nothing left to show in any of its three tiles - the row
    /// itself collapses rather than showing an empty placeholder (docs/superpowers/specs/2026-09-23-
    /// insights-redesign-design.md's Today tab section).</summary>
    public bool HeroRowEmpty => !Goals.HasGoals && ContinueEmpty && !HasRecommendations;

    public string GapLine => GapCount == 0
        ? "No near-complete runs with holes."
        : $"{GapCount} near-complete {(GapCount == 1 ? "run has" : "runs have")} a few issues missing.";

    // --- Drop-off watch (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.3) ---

    private DropOffWatch? _dropOffCache;

    /// <summary>Series now one issue short of where the reader usually stops. Empty hides the card.</summary>
    public ObservableCollection<AttentionRow> DropOffRows { get; } = new();

    public bool HasDropOff => DropOffRows.Count > 0;

    /// <summary>"You often stop after about 3 issues. 4 series are at issue 2."</summary>
    [ObservableProperty]
    private string _dropOffHeadline = string.Empty;

    // A row opens through OpenContinueCommand: its next issue in the reader when it has one, else its series.
    private void PopulateDropOff(TodayLoad load)
    {
        DropOffRows.Clear();
        if (load.DropOff is { } watch)
        {
            foreach (var s in watch.AtRisk)
            {
                string subtitle = s.IssuesRead == 0 ? "On the first issue" : s.IssuesRead == 1 ? "1 issue read" : $"{s.IssuesRead} issues read";
                DropOffRows.Add(new AttentionRow(s.SeriesName, subtitle, s.NextIssueId, s.SeriesId, load.CoverKeys.GetValueOrDefault(s.SeriesId)));
            }

            DropOffHeadline = watch.Headline;
        }

        OnPropertyChanged(nameof(HasDropOff));
    }

    /// <summary>Everything the Today tab reads from the database for one refresh, built by <see cref="BuildToday"/>.</summary>
    private sealed record TodayLoad(
        InsightsSnapshot Snapshot,
        DropOffWatch? DropOff,
        Dictionary<int, string?> CoverKeys,
        string? RecommendationsSeedName,
        IReadOnlyList<InsightsRecommendationCard> Recommendations);

    // Bumped by every refresh and by a cache invalidation, so a background load that was overtaken is dropped.
    private int _refreshGeneration;

    /// <summary>Reloads the screen synchronously on the calling thread. Prefer <see cref="RefreshAsync"/> on navigation.</summary>
    public void Refresh()
    {
        _refreshGeneration++;
        ApplyToday(BuildToday(_cache, _dropOffCache, _nowUtc()));
    }

    /// <summary>Same as <see cref="Refresh"/>, but the queries and card mapping run on a thread-pool thread; only the
    /// collection repopulation touches the UI thread. What the screen showed last stays up until the new data lands.
    /// Logs and falls back to a synchronous load on failure.</summary>
    public async Task RefreshAsync()
    {
        int generation = ++_refreshGeneration;
        try
        {
            var cache = _cache;
            var dropOffCache = _dropOffCache;
            var now = _nowUtc();
            var load = await Task.Run(() => BuildToday(cache, dropOffCache, now)).ConfigureAwait(true);
            if (generation == _refreshGeneration)
            {
                ApplyToday(load);
            }
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Insights async load failed ({ex.GetType().Name}: {ex.Message}) - loading synchronously.");
            if (generation == _refreshGeneration)
            {
                Refresh();
            }
        }
    }

    /// <summary>Every database read for one Today refresh, safe off the UI thread: no observable state touched.
    /// <paramref name="cachedSnapshot"/> non-null means the session cache is still good and only the recommendations and
    /// cover keys are read again.</summary>
    private static TodayLoad BuildToday(InsightsSnapshot? cachedSnapshot, DropOffWatch? cachedDropOff, DateTime nowUtc)
    {
        using var context = PaperbunkrDb.CreateContext();
        var snapshot = cachedSnapshot;
        var dropOff = cachedDropOff;
        if (snapshot is null)
        {
            snapshot = InsightsResolver.Build(context, nowUtc);

            try
            {
                dropOff = DropOffResolver.Build(context, nowUtc);
            }
            catch (Exception ex)
            {
                // A nudge, never worth failing the Today tab over.
                dropOff = null;
                DiagnosticsService.LogMilestone($"Insights: drop-off watch failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        Dictionary<int, string?> coverKeys;
        // One query for every series named anywhere on the tab (docs/superpowers/specs/2026-09-23-insights-redesign-
        // design.md's global cover rule), reusing SeriesCardSample.FromSeries's own cover-issue logic.
        var seriesIds = snapshot.Continue.Select(s => s.SeriesId)
            .Concat(snapshot.AlmostDone.Select(s => s.SeriesId))
            .Concat(snapshot.DiveIn.Select(s => s.SeriesId))
            .Concat(snapshot.Gaps.Select(g => g.SeriesId))
            .Concat(dropOff?.AtRisk.Select(s => s.SeriesId) ?? Enumerable.Empty<int>())
            .Distinct()
            .ToList();
        coverKeys = seriesIds.Count == 0
            ? new Dictionary<int, string?>()
            : context.Series
                .AsNoTracking()
                .Include(s => s.Issues)
                .Where(s => seriesIds.Contains(s.Id))
                .ToDictionary(s => s.Id, s => SeriesCardSample.FromSeries(s).CoverKey);

        var cards = new List<InsightsRecommendationCard>();
        string? seedName = null;
        var seed = InsightsRecommendationResolver.GetSeedWithRecommendations(context);
        if (seed is not null)
        {
            var targetIds = seed.Recommendations.Select(r => r.TargetSeriesId).ToList();
            var targetSeriesById = context.Series
                .Include(s => s.Issues)
                .Where(s => targetIds.Contains(s.Id))
                .ToDictionary(s => s.Id);

            foreach (var recommendation in seed.Recommendations)
            {
                if (targetSeriesById.TryGetValue(recommendation.TargetSeriesId, out var target))
                {
                    cards.Add(new InsightsRecommendationCard(SeriesCardSample.FromSeries(target), recommendation.Explanation));
                }
            }

            seedName = cards.Count > 0 ? seed.SeedSeriesName : null;
        }

        return new TodayLoad(snapshot, dropOff, coverKeys, seedName, cards);
    }

    private void ApplyToday(TodayLoad load)
    {
        _cache = load.Snapshot;
        _dropOffCache = load.DropOff;

        PopulateDropOff(load);

        Snapshot = load.Snapshot;
        PopulateLists(load);
        Goals.Refresh();

        Recommendations.Clear();
        foreach (var card in load.Recommendations)
        {
            Recommendations.Add(card);
        }

        RecommendationsSeedName = load.RecommendationsSeedName;

        foreach (var name in new[]
        {
            nameof(HasRecommendations), nameof(PrimaryRecommendation), nameof(SecondaryRecommendations), nameof(HasSecondaryRecommendations),
            nameof(ContinueCount), nameof(AlmostDoneCount), nameof(DiveInCount), nameof(GapCount),
            nameof(ContinueEmpty), nameof(AlmostDoneEmpty), nameof(DiveInEmpty), nameof(GapEmpty),
            nameof(ReadingAllClear), nameof(ReadingSectionEmpty), nameof(GapLine), nameof(PrimaryContinueRow), nameof(HeroRowEmpty),
            nameof(HasSpotlight), nameof(SpotlightIsContinue), nameof(SpotlightHeading), nameof(SpotlightTitle), nameof(SpotlightSubtitle),
            nameof(SpotlightCoverKey), nameof(SpotlightActionLabel),
        })
        {
            OnPropertyChanged(name);
        }

        if (IsTrendsTabSelected)
        {
            Stats.Refresh();
        }

        if (IsHistoryTabSelected)
        {
            History.Refresh();
        }

        if (IsRecapTabSelected)
        {
            Recap.Refresh();
        }
    }

    private void PopulateLists(TodayLoad load)
    {
        var snap = load.Snapshot;
        var coverKeyBySeriesId = load.CoverKeys;

        ContinueRows.Clear();
        foreach (var s in snap.Continue)
        {
            ContinueRows.Add(new AttentionRow(s.SeriesName, s.Subtitle, s.ResumeIssueId, s.SeriesId, coverKeyBySeriesId.GetValueOrDefault(s.SeriesId)));
        }

        AlmostDoneRows.Clear();
        foreach (var s in snap.AlmostDone)
        {
            AlmostDoneRows.Add(new AttentionRow(s.SeriesName, s.Subtitle, null, s.SeriesId, coverKeyBySeriesId.GetValueOrDefault(s.SeriesId)));
        }

        DiveInRows.Clear();
        foreach (var s in snap.DiveIn)
        {
            DiveInRows.Add(new AttentionRow(s.SeriesName, s.Subtitle, null, s.SeriesId, coverKeyBySeriesId.GetValueOrDefault(s.SeriesId)));
        }

        GapRows.Clear();
        foreach (var g in snap.Gaps)
        {
            GapRows.Add(new GapRow(g.SeriesName, FormatMissing(g.MissingNumbers), g.SeriesId, coverKeyBySeriesId.GetValueOrDefault(g.SeriesId)));
        }
    }

    private static string FormatMissing(IReadOnlyList<int> missing)
    {
        var shown = missing.Take(6).Select(n => "#" + n);
        string s = string.Join(", ", shown);
        return missing.Count > 6 ? $"{s} +{missing.Count - 6}" : s;
    }

    [RelayCommand]
    private void OpenContinue(AttentionRow? row)
    {
        if (row?.ResumeIssueId is { } id)
        {
            _goReaderForIssue(id);
        }
        else if (row is not null)
        {
            _goDetailForSeries(row.SeriesId);
        }
    }

    [RelayCommand]
    private void OpenSeries(int seriesId) => _goDetailForSeries(seriesId);
}

public sealed record AttentionRow(string Title, string Subtitle, int? ResumeIssueId, int SeriesId, string? CoverKey)
{
    /// <summary>The part of <see cref="Subtitle"/> before its first " · " ("own 100 issues · #1-100, never opened" -> "own 100 issues"): what fits under a cover on a shelf.</summary>
    public string ShortSubtitle
    {
        get
        {
            int cut = Subtitle.IndexOf(" · ", StringComparison.Ordinal);
            return cut > 0 ? Subtitle[..cut] : Subtitle;
        }
    }
}

public sealed record GapRow(string Title, string Missing, int SeriesId, string? CoverKey);

/// <summary>One tile in the "Because you finished X" section (docs/superpowers/specs/2026-09-23-insights-
/// recommendations-surface-design.md) - <see cref="Card"/> reuses the same <c>SeriesCardSample</c>
/// cover/title/badge mapping <c>HomeScreenViewModel</c>'s own "Because You Read" rows already use.</summary>
public sealed record InsightsRecommendationCard(SeriesCardSample Card, string Explanation);
