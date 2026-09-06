using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Insights dashboard (docs/superpowers/specs/2026-09-05-insights-dashboard-design.md).
/// Holds one <see cref="InsightsSnapshot"/> per range for the session, rebuilt on first view of a
/// range and dropped whenever a reading event fires while the app is running. All computation is in
/// <see cref="InsightsResolver"/>; this class is presentation glue + the session cache.
/// </summary>
public partial class InsightsScreenViewModel : ViewModelBase
{
    private readonly Action<int> _goReaderForIssue;
    private readonly Action<int> _goDetailForSeries;
    private readonly Action<string> _goLibraryWithSearch;
    private readonly Func<DateTime> _nowUtc;
    private readonly Dictionary<InsightsRange, InsightsSnapshot> _cache = new();

    public InsightsScreenViewModel(
        Action<int> goReaderForIssue,
        Action<int> goDetailForSeries,
        Action<string> goLibraryWithSearch,
        IReadingEventRecorder? readingEventRecorder = null,
        Func<DateTime>? nowUtc = null)
    {
        _goReaderForIssue = goReaderForIssue;
        _goDetailForSeries = goDetailForSeries;
        _goLibraryWithSearch = goLibraryWithSearch;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);

        if (readingEventRecorder is not null)
        {
            readingEventRecorder.ReadingEventRecorded += () =>
            {
                _cache.Clear();
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

    [ObservableProperty]
    private InsightsRange _range = InsightsRange.Days90;

    [ObservableProperty]
    private InsightsSnapshot? _snapshot;

    public IReadOnlyList<RangeOption> RangeOptions { get; } = new[]
    {
        new RangeOption(InsightsRange.Days30, "30d"),
        new RangeOption(InsightsRange.Days90, "90d") { IsActive = true },
        new RangeOption(InsightsRange.Months12, "12mo"),
        new RangeOption(InsightsRange.AllTime, "All time"),
    };

    [RelayCommand]
    private void SetRange(InsightsRange value) => Range = value;

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

    /// <summary>Nothing to read-next in any of the three "Reading" cards (gaps live in At a Glance, not here).</summary>
    public bool ReadingAllClear => ContinueEmpty && AlmostDoneEmpty && DiveInEmpty;

    public string GapLine => GapCount == 0
        ? "No near-complete runs with holes."
        : $"{GapCount} near-complete {(GapCount == 1 ? "run has" : "runs have")} a few issues missing.";

    public int CompositionMax => Math.Max(1, Snapshot?.Composition.ByPublisher.Select(s => s.Count).DefaultIfEmpty(1).Max() ?? 1);

    public bool HasPaceData => Snapshot?.Pace.Any(b => b.Finished > 0 || b.Pages > 0) == true;
    public bool HasStreakData => (Snapshot?.ReadingDayStreak.Longest ?? 0) > 0;
    public bool HasRatings => Snapshot?.Ratings.Any(r => r.Count > 0) == true;

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
            snap = InsightsResolver.Build(context, Range, _nowUtc());
            _cache[Range] = snap;
        }

        Snapshot = snap;
        PopulateLists(snap);

        foreach (var name in new[]
        {
            nameof(ContinueCount), nameof(AlmostDoneCount), nameof(DiveInCount), nameof(GapCount),
            nameof(ContinueEmpty), nameof(AlmostDoneEmpty), nameof(DiveInEmpty), nameof(GapEmpty),
            nameof(ReadingAllClear), nameof(GapLine),
            nameof(HasPaceData), nameof(HasStreakData), nameof(HasRatings), nameof(CompositionMax),
        })
        {
            OnPropertyChanged(name);
        }

        PaceOrRatingsChanged?.Invoke(snap);
    }

    /// <summary>Raised after <see cref="Refresh"/> so the view's code-behind can re-render the two
    /// ScottPlot bar charts (imperative API - no binding).</summary>
    public event Action<InsightsSnapshot>? PaceOrRatingsChanged;

    private void PopulateLists(InsightsSnapshot snap)
    {
        ContinueRows.Clear();
        foreach (var s in snap.Continue)
        {
            ContinueRows.Add(new AttentionRow(s.SeriesName, s.Subtitle, s.ResumeIssueId, s.SeriesId));
        }

        AlmostDoneRows.Clear();
        foreach (var s in snap.AlmostDone)
        {
            AlmostDoneRows.Add(new AttentionRow(s.SeriesName, s.Subtitle, null, s.SeriesId));
        }

        DiveInRows.Clear();
        foreach (var s in snap.DiveIn)
        {
            DiveInRows.Add(new AttentionRow(s.SeriesName, s.Subtitle, null, s.SeriesId));
        }

        GapRows.Clear();
        foreach (var g in snap.Gaps)
        {
            GapRows.Add(new GapRow(g.SeriesName, FormatMissing(g.MissingNumbers), g.SeriesId));
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

    [RelayCommand]
    private void OpenCompositionValue(string? value)
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

public sealed record AttentionRow(string Title, string Subtitle, int? ResumeIssueId, int SeriesId);

public sealed record GapRow(string Title, string Missing, int SeriesId);
