using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
/// (the MangaBaka-style analytics, hosted by <see cref="Stats"/>). All computation is in
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
        IActivityService? activity = null)
    {
        _goReaderForIssue = goReaderForIssue;
        _goDetailForSeries = goDetailForSeries;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        _openNewGoalDialog = openNewGoalDialog;
        Activity = activity;
        Stats = new StatsScreenViewModel(goLibraryWithSearch, readingEventRecorder, nowUtc);
        Recap = new RecapViewModel(readingEventRecorder, nowUtc);
        Goals = new GoalsViewModel(dialogs, readingEventRecorder, nowUtc) { Activity = activity };

        if (readingEventRecorder is not null)
        {
            readingEventRecorder.ReadingEventRecorded += () =>
            {
                _cache = null;
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

    [RelayCommand]
    private void AddGoal() => _openNewGoalDialog();

    [ObservableProperty]
    private bool _isTrendsTabSelected;

    [ObservableProperty]
    private bool _isRecapTabSelected;

    partial void OnIsTrendsTabSelectedChanged(bool value)
    {
        if (value)
        {
            IsRecapTabSelected = false;
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
        }

        Recap.IsActive = value;
        if (value)
        {
            Recap.Refresh();
        }

        OnPropertyChanged(nameof(IsTodayTabSelected));
    }

    /// <summary>True when neither the Trends nor the Recap tab is selected - the third, implicit state
    /// of what used to be a plain <c>!IsTrendsTabSelected</c> 2-way toggle.</summary>
    public bool IsTodayTabSelected => !IsTrendsTabSelected && !IsRecapTabSelected;

    [RelayCommand]
    private void SelectTodayTab()
    {
        IsTrendsTabSelected = false;
        IsRecapTabSelected = false;
    }

    [RelayCommand]
    private void SelectTrendsTab() => IsTrendsTabSelected = true;

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

    /// <summary>True once the hero row has nothing left to show in any of its three tiles - the row
    /// itself collapses rather than showing an empty placeholder (docs/superpowers/specs/2026-09-23-
    /// insights-redesign-design.md's Today tab section).</summary>
    public bool HeroRowEmpty => !Goals.HasGoals && ContinueEmpty && !HasRecommendations;

    public string GapLine => GapCount == 0
        ? "No near-complete runs with holes."
        : $"{GapCount} near-complete {(GapCount == 1 ? "run has" : "runs have")} a few issues missing.";

    public void Refresh()
    {
        if (_cache is null)
        {
            using var context = PaperbunkrDb.CreateContext();
            _cache = InsightsResolver.Build(context, _nowUtc());
        }

        Snapshot = _cache;
        PopulateLists(_cache);
        Goals.Refresh();
        RefreshRecommendations();

        foreach (var name in new[]
        {
            nameof(ContinueCount), nameof(AlmostDoneCount), nameof(DiveInCount), nameof(GapCount),
            nameof(ContinueEmpty), nameof(AlmostDoneEmpty), nameof(DiveInEmpty), nameof(GapEmpty),
            nameof(ReadingAllClear), nameof(ReadingSectionEmpty), nameof(GapLine), nameof(PrimaryContinueRow), nameof(HeroRowEmpty),
        })
        {
            OnPropertyChanged(name);
        }

        if (IsTrendsTabSelected)
        {
            Stats.Refresh();
        }

        if (IsRecapTabSelected)
        {
            Recap.Refresh();
        }
    }

    private void PopulateLists(InsightsSnapshot snap)
    {
        var seriesIds = snap.Continue.Select(s => s.SeriesId)
            .Concat(snap.AlmostDone.Select(s => s.SeriesId))
            .Concat(snap.DiveIn.Select(s => s.SeriesId))
            .Concat(snap.Gaps.Select(g => g.SeriesId))
            .Distinct()
            .ToList();
        var coverKeyBySeriesId = ResolveCoverKeys(seriesIds);

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

    /// <summary>Batch cover-key lookup for the Today tab's row-level thumbnails (docs/superpowers/specs/
    /// 2026-09-23-insights-redesign-design.md's global cover rule) - one query for every series named
    /// anywhere on the tab, reusing <see cref="SeriesCardSample.FromSeries"/>'s own cover-issue logic
    /// rather than duplicating it.</summary>
    private static Dictionary<int, string?> ResolveCoverKeys(IReadOnlyList<int> seriesIds)
    {
        if (seriesIds.Count == 0)
        {
            return new Dictionary<int, string?>();
        }

        using var context = PaperbunkrDb.CreateContext();
        return context.Series
            .Include(s => s.Issues)
            .Where(s => seriesIds.Contains(s.Id))
            .ToDictionary(s => s.Id, s => SeriesCardSample.FromSeries(s).CoverKey);
    }

    private void RefreshRecommendations()
    {
        using var context = PaperbunkrDb.CreateContext();
        var seed = InsightsRecommendationResolver.GetSeedWithRecommendations(context);

        Recommendations.Clear();
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
                    Recommendations.Add(new InsightsRecommendationCard(SeriesCardSample.FromSeries(target), recommendation.Explanation));
                }
            }
        }

        RecommendationsSeedName = Recommendations.Count > 0 ? seed!.SeedSeriesName : null;
        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(PrimaryRecommendation));
        OnPropertyChanged(nameof(SecondaryRecommendations));
        OnPropertyChanged(nameof(HasSecondaryRecommendations));
        OnPropertyChanged(nameof(HeroRowEmpty));
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

public sealed record AttentionRow(string Title, string Subtitle, int? ResumeIssueId, int SeriesId, string? CoverKey);

public sealed record GapRow(string Title, string Missing, int SeriesId, string? CoverKey);

/// <summary>One tile in the "Because you finished X" section (docs/superpowers/specs/2026-09-23-insights-
/// recommendations-surface-design.md) - <see cref="Card"/> reuses the same <c>SeriesCardSample</c>
/// cover/title/badge mapping <c>HomeScreenViewModel</c>'s own "Because You Read" rows already use.</summary>
public sealed record InsightsRecommendationCard(SeriesCardSample Card, string Explanation);
