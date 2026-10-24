using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The five Insights-pitch cards on the Trends tab (docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md):
/// reading rhythm (Activity), "Where you stop" and Storage (Composition), "You love these" (Top Lists). Rhythm is range-scoped and
/// computed in <see cref="Refresh"/> with the rest; the other three are range-independent, loaded off the UI thread the first time their
/// group is shown, and cached. Reading activity invalidates drop-off and affinity; storage is not reading-driven, so it only reloads
/// when the screen's data is explicitly refreshed (<see cref="InvalidateStorage"/>).
/// </summary>
public partial class StatsScreenViewModel
{
    private sealed class LazySlot
    {
        public bool Loaded;
        public int Generation;
    }

    private readonly Dictionary<InsightsRange, RhythmData> _rhythmCache = new();
    private readonly LazySlot _dropOffSlot = new();
    private readonly LazySlot _affinitySlot = new();
    private readonly LazySlot _storageSlot = new();

    /// <summary>Opens Library Health › Duplicates; set by <see cref="InsightsScreenViewModel"/>. Null hides the Review button.</summary>
    public Action? OpenDuplicateReview { get; set; }

    /// <summary>Opens a series' Detail screen; set by <see cref="InsightsScreenViewModel"/>.</summary>
    public Action<int>? GoDetailForSeries { get; set; }

    /// <summary>Tests set this so the lazy cards load inline instead of on the thread pool.</summary>
    public bool RunSynchronously { get; set; }

    // --- Rhythm ---------------------------------------------------------------------------------

    [ObservableProperty]
    private RhythmData _rhythm = RhythmData.Empty;

    public bool HasRhythmChart => Rhythm.HasChart;

    public bool RhythmIsEmpty => !Rhythm.HasChart;

    public int RhythmMax
    {
        get
        {
            int max = 0;
            foreach (int v in Rhythm.SessionsByWeekdayHour)
            {
                max = Math.Max(max, v);
            }

            return max;
        }
    }

    public int RhythmLengthMax => Math.Max(1, Rhythm.LengthBuckets.Select(b => b.Count).DefaultIfEmpty(1).Max());

    public bool HasSessionLengths => Rhythm.MeasuredSessionCount > 0;

    public string RhythmCoverageText => Rhythm.SessionCount == 0
        ? string.Empty
        : $"{Rhythm.SessionCount:N0} sessions · session length based on {Rhythm.MeasuredSessionCount:N0} with timing (recorded since 6 Oct 2026)";

    public string RhythmTypicalText => Rhythm.AverageSessionMinutes is { } m
        ? $"Typical session {Math.Round(m):0} min"
        : "No timed sessions yet";

    public string RhythmPeakText => Rhythm.PeakWeekday is { } d && Rhythm.PeakHour is { } h
        ? $"Most reading starts {CultureInfo.InvariantCulture.DateTimeFormat.DayNames[d]} {PartOfDay(h)}s"
        : string.Empty;

    internal static string PartOfDay(int hour) => hour switch
    {
        < 5 => "night",
        < 12 => "morning",
        < 17 => "afternoon",
        < 21 => "evening",
        _ => "night",
    };

    partial void OnRhythmChanged(RhythmData value)
    {
        foreach (var name in new[]
        {
            nameof(HasRhythmChart), nameof(RhythmIsEmpty), nameof(RhythmMax), nameof(RhythmLengthMax), nameof(HasSessionLengths),
            nameof(RhythmCoverageText), nameof(RhythmTypicalText), nameof(RhythmPeakText),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private void LoadRhythm()
    {
        if (!_rhythmCache.TryGetValue(Range, out var data))
        {
            try
            {
                using var context = PaperbunkrDb.CreateContext();
                data = RhythmResolver.Build(context, Range, _nowUtc());
            }
            catch (Exception)
            {
                data = RhythmData.Empty;
            }

            _rhythmCache[Range] = data;
        }

        Rhythm = data;
    }

    // --- Where you stop (drop-off breakdown) ----------------------------------------------------

    [ObservableProperty]
    private DropOffBreakdown _dropOff = DropOffBreakdown.Empty;

    [ObservableProperty]
    private DropOffDimension _dropOffDimension = DropOffDimension.Publisher;

    public IReadOnlyList<DropOffDimensionOption> DropOffDimensionOptions { get; } = new[]
    {
        new DropOffDimensionOption(DropOffDimension.Publisher, "Publisher") { IsActive = true },
        new DropOffDimensionOption(DropOffDimension.Genre, "Genre"),
        new DropOffDimensionOption(DropOffDimension.ContentType, "Type"),
    };

    [RelayCommand]
    private void SetDropOffDimension(DropOffDimension value) => DropOffDimension = value;

    public bool HasDropOff => DropOff.HasData;

    public string DropOffSummary => DropOff.HasData
        ? $"{DropOff.StoppedTotal:N0} stopped · {DropOff.FinishedTotal:N0} finished"
        : string.Empty;

    public IReadOnlyList<DropOffRowView> DropOffRows => (DropOffDimension switch
    {
        DropOffDimension.Genre => DropOff.ByGenre,
        DropOffDimension.ContentType => DropOff.ByContentType,
        _ => DropOff.ByPublisher,
    }).Take(8).Select(r => new DropOffRowView(
        r.Label, r.StoppedRate, $"{r.StoppedRate * 100:0}% stopped · {r.Stopped} of {r.Decided}", r.Label != DropOffBreakdownResolver.OtherLabel)).ToList();

    public IReadOnlyList<BarRowView> DropOffStallBars
    {
        get
        {
            int max = Math.Max(1, DropOff.StallPoints.Select(b => b.Count).DefaultIfEmpty(1).Max());
            return DropOff.StallPoints.Select(b => new BarRowView(b.Label, (double)b.Count / max, b.Count.ToString("N0", CultureInfo.InvariantCulture))).ToList();
        }
    }

    partial void OnDropOffChanged(DropOffBreakdown value) => RaiseDropOff();

    partial void OnDropOffDimensionChanged(DropOffDimension value)
    {
        foreach (var option in DropOffDimensionOptions)
        {
            option.IsActive = option.Value == value;
        }

        RaiseDropOff();
    }

    private void RaiseDropOff()
    {
        OnPropertyChanged(nameof(HasDropOff));
        OnPropertyChanged(nameof(DropOffSummary));
        OnPropertyChanged(nameof(DropOffRows));
        OnPropertyChanged(nameof(DropOffStallBars));
    }

    // --- You love these (affinity) --------------------------------------------------------------

    [ObservableProperty]
    private AffinityData _affinity = AffinityData.Empty;

    [ObservableProperty]
    private AffinityDimension _affinityDimension = AffinityDimension.Authors;

    public IReadOnlyList<AffinityDimensionOption> AffinityDimensionOptions { get; } = new[]
    {
        new AffinityDimensionOption(AffinityDimension.Authors, "Authors") { IsActive = true },
        new AffinityDimensionOption(AffinityDimension.Artists, "Artists"),
        new AffinityDimensionOption(AffinityDimension.Publishers, "Publishers"),
    };

    [RelayCommand]
    private void SetAffinityDimension(AffinityDimension value) => AffinityDimension = value;

    public bool HasAffinity => Affinity.HasData;

    public bool AffinityRowsEmpty => AffinityRows.Count == 0;

    public IReadOnlyList<AffinityRowView> AffinityRows => (AffinityDimension switch
    {
        AffinityDimension.Artists => Affinity.Artists,
        AffinityDimension.Publishers => Affinity.Publishers,
        _ => Affinity.Authors,
    }).Select(e => new AffinityRowView(
        e.Name,
        Math.Clamp(e.Score / 5.0, 0, 1),
        $"★ {e.AverageRating:0.0} · {e.CompletionRate * 100:0}% read · {e.Series} series")).ToList();

    partial void OnAffinityChanged(AffinityData value) => RaiseAffinity();

    partial void OnAffinityDimensionChanged(AffinityDimension value)
    {
        foreach (var option in AffinityDimensionOptions)
        {
            option.IsActive = option.Value == value;
        }

        RaiseAffinity();
    }

    private void RaiseAffinity()
    {
        OnPropertyChanged(nameof(HasAffinity));
        OnPropertyChanged(nameof(AffinityRows));
        OnPropertyChanged(nameof(AffinityRowsEmpty));
    }

    // --- Storage --------------------------------------------------------------------------------

    [ObservableProperty]
    private StorageData _storage = StorageData.Empty;

    public bool HasStorage => Storage.HasData;

    public string StorageTotalText => FormatBytes(Storage.TotalBytes);

    public string StorageSubText
    {
        get
        {
            var parts = new List<string> { $"across {Storage.MeasuredFiles:N0} comics" };
            if (Storage.UnmeasuredFiles > 0)
            {
                parts.Add($"{Storage.UnmeasuredFiles:N0} not measured yet");
            }

            if (Storage.BookCount > 0)
            {
                parts.Add($"{Storage.BookCount:N0} books (size not tracked)");
            }

            return string.Join(" · ", parts);
        }
    }

    public IReadOnlyList<BarRowView> StorageTypeRows => ToBars(Storage.ByFileType);

    public IReadOnlyList<BarRowView> StoragePublisherRows => ToBars(Storage.ByPublisher);

    public IReadOnlyList<LargeFileRowView> StorageLargestRows => Storage.Largest
        .Select(l => new LargeFileRowView(l.SeriesId, l.Title, $"{FormatBytes(l.Bytes)} · {l.FileType}")).ToList();

    public bool HasReclaimable => Storage.ReclaimableBytes > 0;

    public bool CanReviewDuplicates => HasReclaimable && OpenDuplicateReview is not null;

    public string ReclaimableText => HasReclaimable
        ? $"Duplicates could free up to {FormatBytes(Storage.ReclaimableBytes)} across {Storage.DuplicateGroups:N0} groups"
        : string.Empty;

    [RelayCommand]
    private void ReviewDuplicates() => OpenDuplicateReview?.Invoke();

    [RelayCommand]
    private void OpenLargeFile(LargeFileRowView? row)
    {
        if (row is not null)
        {
            GoDetailForSeries?.Invoke(row.SeriesId);
        }
    }

    partial void OnStorageChanged(StorageData value)
    {
        foreach (var name in new[]
        {
            nameof(HasStorage), nameof(StorageTotalText), nameof(StorageSubText), nameof(StorageTypeRows), nameof(StoragePublisherRows),
            nameof(StorageLargestRows), nameof(HasReclaimable), nameof(CanReviewDuplicates), nameof(ReclaimableText),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private static IReadOnlyList<BarRowView> ToBars(IReadOnlyList<StorageSlice> slices)
    {
        long max = Math.Max(1, slices.Select(s => s.Bytes).DefaultIfEmpty(1).Max());
        return slices.Select(s => new BarRowView(s.Label, (double)s.Bytes / max, FormatBytes(s.Bytes))).ToList();
    }

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 40 => $"{bytes / (double)(1L << 40):0.0} TB",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };

    // --- Lazy loading ---------------------------------------------------------------------------

    /// <summary>Loads whichever lazy cards belong to the group on show, once. Called when a group is selected and after each refresh.</summary>
    private void EnsureGroupLoaded()
    {
        if (IsCompositionGroupSelected)
        {
            _ = LoadSlotAsync(_dropOffSlot, () => { using var c = PaperbunkrDb.CreateContext(); return DropOffBreakdownResolver.Build(c, _nowUtc()); },
                v => DropOff = v);
            _ = LoadSlotAsync(_storageSlot, () => { using var c = PaperbunkrDb.CreateContext(); return StorageResolver.Build(c); },
                v => Storage = v);
        }
        else if (IsTopListsGroupSelected)
        {
            _ = LoadSlotAsync(_affinitySlot, () => { using var c = PaperbunkrDb.CreateContext(); return AffinityResolver.Build(c); },
                v => Affinity = v);
        }
    }

    private async Task LoadSlotAsync<T>(LazySlot slot, Func<T> work, Action<T> apply)
    {
        if (slot.Loaded)
        {
            return;
        }

        slot.Loaded = true; // a failure leaves the card empty rather than retrying on every tab switch
        int generation = ++slot.Generation;
        T result;
        try
        {
            result = RunSynchronously ? work() : await Task.Run(work);
        }
        catch (Exception)
        {
            return;
        }

        if (generation == slot.Generation)
        {
            apply(result);
        }
    }

    /// <summary>Reading activity changed: rhythm, drop-off and affinity are stale (storage is not).</summary>
    private void InvalidateReadingDerived()
    {
        _rhythmCache.Clear();
        _dropOffSlot.Loaded = false;
        _dropOffSlot.Generation++;
        _affinitySlot.Loaded = false;
        _affinitySlot.Generation++;
    }

    /// <summary>The library changed (scan, import, delete): storage totals and duplicate savings are stale.</summary>
    public void InvalidateStorage()
    {
        _storageSlot.Loaded = false;
        _storageSlot.Generation++;
    }
}

public enum DropOffDimension { Publisher, Genre, ContentType }

public enum AffinityDimension { Authors, Artists, Publishers }

public partial class ChipOption<T> : ObservableObject
{
    public ChipOption(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <param name="StoppedFraction">0..1; drives the width of the stopped segment.</param>
/// <param name="CanFilter">False for the folded "Other" row, which has no single Library search.</param>
public sealed record DropOffRowView(string Label, double StoppedFraction, string Text, bool CanFilter);

public sealed record BarRowView(string Label, double Fraction, string ValueText);

public sealed record AffinityRowView(string Name, double Fraction, string Text);

public sealed record LargeFileRowView(int SeriesId, string Title, string Text);

public sealed class DropOffDimensionOption : ChipOption<DropOffDimension>
{
    public DropOffDimensionOption(DropOffDimension value, string label) : base(value, label)
    {
    }
}

public sealed class AffinityDimensionOption : ChipOption<AffinityDimension>
{
    public AffinityDimensionOption(AffinityDimension value, string label) : base(value, label)
    {
    }
}
