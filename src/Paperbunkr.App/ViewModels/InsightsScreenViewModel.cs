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
/// The Insights screen (docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md §5, revised
/// after the first on-screen pass to keep Stats a *tab* on this screen rather than a separate
/// nav-rail destination - matches this project's established "discrete section switching over
/// scattering related content across destinations" preference). Two tabs sharing one nav-rail entry:
/// **Overview** (READING attention cards + Collection health - unchanged) and **Stats** (the
/// MangaBaka-style analytics, hosted by <see cref="Stats"/>). All computation is in
/// <see cref="InsightsResolver"/>/<see cref="StatsResolver"/>; this class is presentation glue + the
/// session cache for the Overview tab (Stats owns its own cache).
/// </summary>
public partial class InsightsScreenViewModel : ViewModelBase
{
    private readonly Action<int> _goReaderForIssue;
    private readonly Action<int> _goDetailForSeries;
    private readonly Func<DateTime> _nowUtc;
    private InsightsSnapshot? _cache;

    public InsightsScreenViewModel(
        Action<int> goReaderForIssue,
        Action<int> goDetailForSeries,
        Action<string> goLibraryWithSearch,
        IReadingEventRecorder? readingEventRecorder = null,
        Func<DateTime>? nowUtc = null)
    {
        _goReaderForIssue = goReaderForIssue;
        _goDetailForSeries = goDetailForSeries;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        Stats = new StatsScreenViewModel(goLibraryWithSearch, readingEventRecorder, nowUtc);

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

    /// <summary>The Stats tab's own view-model - separate cache/range from the Overview tab above,
    /// since most of its tiles are range-aware and none of Overview's are.</summary>
    public StatsScreenViewModel Stats { get; }

    [ObservableProperty]
    private bool _isStatsTabSelected;

    partial void OnIsStatsTabSelectedChanged(bool value)
    {
        Stats.IsActive = value;
        if (value)
        {
            Stats.Refresh();
        }
    }

    [RelayCommand]
    private void SelectOverviewTab() => IsStatsTabSelected = false;

    [RelayCommand]
    private void SelectStatsTab() => IsStatsTabSelected = true;

    [ObservableProperty]
    private InsightsSnapshot? _snapshot;

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

        foreach (var name in new[]
        {
            nameof(ContinueCount), nameof(AlmostDoneCount), nameof(DiveInCount), nameof(GapCount),
            nameof(ContinueEmpty), nameof(AlmostDoneEmpty), nameof(DiveInEmpty), nameof(GapEmpty),
            nameof(ReadingAllClear), nameof(GapLine),
        })
        {
            OnPropertyChanged(name);
        }

        if (IsStatsTabSelected)
        {
            Stats.Refresh();
        }
    }

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
}

public sealed record AttentionRow(string Title, string Subtitle, int? ResumeIssueId, int SeriesId);

public sealed record GapRow(string Title, string Missing, int SeriesId);
