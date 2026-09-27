using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Stats screen (docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md §6-7) - the
/// curiosity/analytics content split out of Insights: lifetime totals, streaks, pace, highlights,
/// activity heatmap, library growth, breakdowns, ratings, and top-N lists. Same shape as
/// <see cref="InsightsScreenViewModel"/>: holds one <see cref="StatsSnapshot"/> per range for the
/// session, rebuilt on first view of a range and dropped whenever a reading event fires while the
/// app is running. All computation is in <see cref="StatsResolver"/>; this class is presentation
/// glue + the session cache.
/// </summary>
public partial class StatsScreenViewModel : ViewModelBase
{
    private readonly Action<string> _goLibraryWithSearch;
    private readonly Func<DateTime> _nowUtc;
    private readonly Dictionary<InsightsRange, StatsSnapshot> _cache = new();

    public StatsScreenViewModel(
        Action<string> goLibraryWithSearch,
        IReadingEventRecorder? readingEventRecorder = null,
        Func<DateTime>? nowUtc = null)
    {
        _goLibraryWithSearch = goLibraryWithSearch;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);

        if (readingEventRecorder is not null)
        {
            readingEventRecorder.ReadingEventRecorded += () =>
            {
                _cache.Clear();
                if (IsActive)
                {
                    Refresh();
                }
            };
        }
    }

    /// <summary>Set by the shell while the Stats screen is the visible lateral screen.</summary>
    public bool IsActive { get; set; }

    [ObservableProperty]
    private InsightsRange _range = InsightsRange.Days90;

    [ObservableProperty]
    private StatsSnapshot? _snapshot;

    /// <summary>Delta badge + sparkline display state for the three range-scoped Reading Activity tiles
    /// (docs/superpowers/specs/2026-09-22-insights-period-over-period-deltas-design.md) - null hides the badge
    /// and sparkline entirely (All time range, or not enough history for a full prior window).</summary>
    [ObservableProperty]
    private TrendDisplay? _finishedTrend;

    [ObservableProperty]
    private TrendDisplay? _paceTrend;

    [ObservableProperty]
    private TrendDisplay? _avgToFinishTrend;

    /// <summary>Library Growth chart's Measure toggle (design §6.4) - pure view state, not part of the
    /// cached <see cref="StatsSnapshot"/>: <see cref="StatsResolver"/> hands back raw
    /// <see cref="GrowthPoint"/>s once per range, and the chart re-buckets them cumulatively whenever
    /// this changes, so switching Issues/Series never needs a re-query. The chart's former Stack-by
    /// toggle (Reading state/Media type/Content rating, each as an overlaid line) was removed
    /// 2026-09-24 - composition breakdowns belong in their own donut/bar cards, not squeezed into a
    /// growth-over-time line chart with a wrapping multi-category legend.</summary>
    [ObservableProperty]
    private GrowthMeasure _growthMeasure = GrowthMeasure.Issues;

    public IReadOnlyList<GrowthMeasureOption> GrowthMeasureOptions { get; } = new[]
    {
        new GrowthMeasureOption(GrowthMeasure.Issues, "Issues") { IsActive = true },
        new GrowthMeasureOption(GrowthMeasure.Series, "Series"),
    };

    [RelayCommand]
    private void SetGrowthMeasure(GrowthMeasure value)
    {
        GrowthMeasure = value;
        foreach (var option in GrowthMeasureOptions)
        {
            option.IsActive = option.Value == value;
        }
    }

    partial void OnGrowthMeasureChanged(GrowthMeasure value) => RaiseChartsChangedIfReady();

    private void RaiseChartsChangedIfReady()
    {
        if (Snapshot is { } snap)
        {
            ChartsChanged?.Invoke(snap);
        }
    }

    public IReadOnlyList<RangeOption> RangeOptions { get; } = new[]
    {
        new RangeOption(InsightsRange.Days30, "30d"),
        new RangeOption(InsightsRange.Days90, "90d") { IsActive = true },
        new RangeOption(InsightsRange.Months12, "12mo"),
        new RangeOption(InsightsRange.AllTime, "All time"),
    };

    [RelayCommand]
    private void SetRange(InsightsRange value) => Range = value;

    /// <summary>The Trends tab's pill sub-nav (docs/superpowers/specs/2026-09-23-insights-redesign-
    /// design.md) - a pure display-side partition of the same fifteen cards this tab already had, no
    /// resolver/data change. Same enum+option-list+command+changed-handler shape as <see cref="Range"/>
    /// above, plus one boolean per group so the XAML can gate each group's container without a
    /// converter, matching this codebase's existing preference (e.g. <c>IsTodayTabSelected</c>).</summary>
    [ObservableProperty]
    private TrendsGroup _selectedTrendsGroup = TrendsGroup.Activity;

    public IReadOnlyList<TrendsGroupOption> TrendsGroupOptions { get; } = new[]
    {
        new TrendsGroupOption(TrendsGroup.Activity, "Activity") { IsActive = true },
        new TrendsGroupOption(TrendsGroup.Composition, "Composition"),
        new TrendsGroupOption(TrendsGroup.TopLists, "Top Lists"),
    };

    public bool IsActivityGroupSelected => SelectedTrendsGroup == TrendsGroup.Activity;

    public bool IsCompositionGroupSelected => SelectedTrendsGroup == TrendsGroup.Composition;

    public bool IsTopListsGroupSelected => SelectedTrendsGroup == TrendsGroup.TopLists;

    [RelayCommand]
    private void SetTrendsGroup(TrendsGroup value) => SelectedTrendsGroup = value;

    partial void OnSelectedTrendsGroupChanged(TrendsGroup value)
    {
        foreach (var option in TrendsGroupOptions)
        {
            option.IsActive = option.Value == value;
        }

        OnPropertyChanged(nameof(IsActivityGroupSelected));
        OnPropertyChanged(nameof(IsCompositionGroupSelected));
        OnPropertyChanged(nameof(IsTopListsGroupSelected));
    }

    public bool HasPaceData => Snapshot?.Pace.Any(b => b.Finished > 0 || b.Pages > 0) == true;

    /// <summary>Whether the Backlog burn-down card should show its chart at all - true for both the
    /// cleared and the trend-line states, false only for the pure "not enough history yet" empty state
    /// (docs/superpowers/specs/2026-09-22-insights-backlog-burndown-design.md).</summary>
    public bool HasBurnDownData => Snapshot?.BurnDown is { IsCleared: true } or { HasEnoughHistory: true };

    /// <summary>The Backlog burn-down card's status line - the cleared/projected-date/not-trending-down
    /// three states (the empty-state message itself is handled directly in XAML via
    /// <see cref="HasBurnDownData"/>, not here).</summary>
    public string BurnDownStatusText => Snapshot?.BurnDown switch
    {
        { IsCleared: true } => "No backlog - all caught up",
        { ProjectedClearDate: { } date } => $"Projected clear: {date:MMM d, yyyy}",
        { HasEnoughHistory: true } => "Not currently trending down",
        _ => string.Empty,
    };

    public bool HasRatings => Snapshot?.Ratings.Any(r => r.Count > 0) == true;

    public bool HasHeatmapData => (Snapshot?.Heatmap.Count ?? 0) > 0;

    public bool HasGrowthData => (Snapshot?.LibraryGrowth.Points.Count ?? 0) > 0;

    public bool HasPublicationYearData => (Snapshot?.PublicationYear.Count ?? 0) > 0;

    public bool HasTopAuthors => (Snapshot?.TopAuthors.Count ?? 0) > 0;

    public bool HasTopArtists => (Snapshot?.TopArtists.Count ?? 0) > 0;

    public int CompositionMax => Math.Max(1, Snapshot?.Composition.ByPublisher.Select(s => s.Count).DefaultIfEmpty(1).Max() ?? 1);

    public int TopAuthorsMax => Math.Max(1, Snapshot?.TopAuthors.Select(s => s.Count).DefaultIfEmpty(1).Max() ?? 1);

    public int TopArtistsMax => Math.Max(1, Snapshot?.TopArtists.Select(s => s.Count).DefaultIfEmpty(1).Max() ?? 1);

    public int TopTagsMax => Math.Max(1, Snapshot?.Composition.ByTags.Select(s => s.Count).DefaultIfEmpty(1).Max() ?? 1);

    public int TopGenresMax => Math.Max(1, Snapshot?.Composition.ByGenre.Select(s => s.Count).DefaultIfEmpty(1).Max() ?? 1);

    partial void OnRangeChanged(InsightsRange value)
    {
        foreach (var option in RangeOptions)
        {
            option.IsActive = option.Value == value;
        }

        Refresh();
    }

    public void Refresh()
    {
        if (!_cache.TryGetValue(Range, out var snap))
        {
            using var context = PaperbunkrDb.CreateContext();
            snap = StatsResolver.Build(context, Range, _nowUtc());
            _cache[Range] = snap;
        }

        Snapshot = snap;
        FinishedTrend = ToDisplay(snap.Trend?.FinishedItems);
        PaceTrend = ToDisplay(snap.Trend?.AvgIssuesPerDay);
        AvgToFinishTrend = ToDisplay(snap.Trend?.AvgDaysToComplete);

        foreach (var name in new[]
        {
            nameof(HasPaceData), nameof(HasBurnDownData), nameof(BurnDownStatusText), nameof(HasRatings), nameof(HasHeatmapData), nameof(HasGrowthData),
            nameof(HasPublicationYearData), nameof(HasTopAuthors), nameof(HasTopArtists),
            nameof(CompositionMax), nameof(TopAuthorsMax), nameof(TopArtistsMax), nameof(TopTagsMax), nameof(TopGenresMax),
        })
        {
            OnPropertyChanged(name);
        }

        ChartsChanged?.Invoke(snap);
    }

    /// <summary>Raised after <see cref="Refresh"/> so the view's code-behind can re-render the
    /// ScottPlot charts (pace, publication year) - imperative API, no binding.</summary>
    public event Action<StatsSnapshot>? ChartsChanged;

    [RelayCommand]
    private void OpenLibraryFilter(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != "Unknown")
        {
            _goLibraryWithSearch(value);
        }
    }

    private static TrendDisplay? ToDisplay(TileTrend? t)
    {
        if (t is null)
        {
            return null;
        }

        if (t.ShowNew)
        {
            return new TrendDisplay(IsUp: true, "new", t.IsGoodDirection, t.SparklinePoints);
        }

        double pct = t.PercentChange ?? 0;
        return new TrendDisplay(IsUp: pct >= 0, $"{Math.Abs(pct):0}%", t.IsGoodDirection, t.SparklinePoints);
    }
}

/// <summary>Bindable delta-badge + sparkline state for one Reading Activity tile (docs/superpowers/specs/2026-
/// 09-22-insights-period-over-period-deltas-design.md), mapped from <see cref="TileTrend"/> by
/// <see cref="StatsScreenViewModel.ToDisplay"/>. <see cref="IsUp"/> picks the arrow glyph (independent of
/// <see cref="IsGood"/>, which picks the brush) and is always true when <see cref="Text"/> is "new".</summary>
public sealed record TrendDisplay(bool IsUp, string Text, bool IsGood, IReadOnlyList<double> Sparkline);

public sealed partial class RangeOption : ObservableObject
{
    public RangeOption(InsightsRange value, string label)
    {
        Value = value;
        Label = label;
    }

    public InsightsRange Value { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>What one point on the Library Growth chart counts as (design §6.4). <c>Series</c>
/// counts each series once (at its earliest-added issue's date) plus each standalone book;
/// <c>Issues</c> counts every individual issue/book file.</summary>
public enum GrowthMeasure { Series, Issues }

public sealed partial class GrowthMeasureOption : ObservableObject
{
    public GrowthMeasureOption(GrowthMeasure value, string label)
    {
        Value = value;
        Label = label;
    }

    public GrowthMeasure Value { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>The Trends tab's pill sub-nav groups (docs/superpowers/specs/2026-09-23-insights-redesign-
/// design.md) - purely a display-side partition of the tab's existing fifteen cards.</summary>
public enum TrendsGroup { Activity, Composition, TopLists }

public sealed partial class TrendsGroupOption : ObservableObject
{
    public TrendsGroupOption(TrendsGroup value, string label)
    {
        Value = value;
        Label = label;
    }

    public TrendsGroup Value { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}
