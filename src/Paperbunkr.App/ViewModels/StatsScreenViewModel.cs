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

    /// <summary>Library Growth chart controls (design §6.4) - pure view state, not part of the
    /// cached <see cref="StatsSnapshot"/>: <see cref="StatsResolver"/> hands back raw
    /// <see cref="GrowthPoint"/>s once per range, and the chart re-buckets them cumulatively
    /// whenever either toggle changes, so switching Measure/Stack-by never needs a re-query.</summary>
    [ObservableProperty]
    private GrowthMeasure _growthMeasure = GrowthMeasure.Issues;

    [ObservableProperty]
    private GrowthStackBy _growthStackBy = GrowthStackBy.Total;

    public IReadOnlyList<GrowthMeasureOption> GrowthMeasureOptions { get; } = new[]
    {
        new GrowthMeasureOption(GrowthMeasure.Issues, "Issues") { IsActive = true },
        new GrowthMeasureOption(GrowthMeasure.Series, "Series"),
    };

    public IReadOnlyList<GrowthStackByOption> GrowthStackByOptions { get; } = new[]
    {
        new GrowthStackByOption(GrowthStackBy.Total, "Total") { IsActive = true },
        new GrowthStackByOption(GrowthStackBy.ReadingStatus, "Reading state"),
        new GrowthStackByOption(GrowthStackBy.MediaType, "Media type"),
        new GrowthStackByOption(GrowthStackBy.ContentRating, "Content rating"),
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

    [RelayCommand]
    private void SetGrowthStackBy(GrowthStackBy value)
    {
        GrowthStackBy = value;
        foreach (var option in GrowthStackByOptions)
        {
            option.IsActive = option.Value == value;
        }
    }

    partial void OnGrowthMeasureChanged(GrowthMeasure value) => RaiseChartsChangedIfReady();

    partial void OnGrowthStackByChanged(GrowthStackBy value) => RaiseChartsChangedIfReady();

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

    public bool HasPaceData => Snapshot?.Pace.Any(b => b.Finished > 0 || b.Pages > 0) == true;

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

        foreach (var name in new[]
        {
            nameof(HasPaceData), nameof(HasRatings), nameof(HasHeatmapData), nameof(HasGrowthData),
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
}

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

/// <summary>Which dimension the Library Growth chart's cumulative lines are split by (design §6.4).
/// <c>Total</c> is a single line; the other three each match a <see cref="GrowthPoint"/> field.</summary>
public enum GrowthStackBy { Total, ReadingStatus, MediaType, ContentRating }

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

public sealed partial class GrowthStackByOption : ObservableObject
{
    public GrowthStackByOption(GrowthStackBy value, string label)
    {
        Value = value;
        Label = label;
    }

    public GrowthStackBy Value { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}
