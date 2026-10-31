using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Library Health's "Chapter alignment" section (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 1):
/// manga series whose chapters differ from MangaDex's. Opening the section only reads the cached lists (no network); "Check now" fetches the
/// missing or stale ones, one MangaDex request at a time. Report-only: nothing here edits an issue. "Not a problem" is a
/// <see cref="HealthDismissals"/> entry keyed on the series and its differing numbers, so a newly missing chapter brings the row back.
/// </summary>
public sealed partial class ChapterAlignmentViewModel : ObservableObject
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<Action> _defer;
    private readonly MangaDexAggregateClient _client;
    private readonly Func<DateTime> _nowUtc;
    private CancellationTokenSource? _checking;

    public ChapterAlignmentViewModel(
        Func<PaperbunkrDbContext>? contextFactory = null,
        Action<Action>? defer = null,
        MangaDexAggregateClient? client = null,
        Func<DateTime>? nowUtc = null)
    {
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _defer = defer ?? (action => Dispatcher.UIThread.Post(action));
        _client = client ?? MangaDexAggregateClient.Shared;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    /// <summary>Opens a series' Detail page. Set by the shell; null in tests and until then.</summary>
    public Action<int>? OpenSeries { get; set; }

    public ObservableCollection<AlignmentRow> Rows { get; } = new();

    public ObservableCollection<DismissedFindingRow> Dismissed { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderDetail), nameof(IsClean))]
    private bool _isScanned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck))]
    private bool _isChecking;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public bool CanCheck => !IsChecking;

    public bool HasRows => Rows.Count > 0;

    public bool HasDismissed => Dismissed.Count > 0;

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>True once the cached lists have been read and nothing differs.</summary>
    public bool IsClean => IsScanned && Rows.Count == 0;

    public string HeaderDetail => !IsScanned
        ? "· Not scanned yet"
        : Rows.Count == 0 ? "· No differences" : $"· {Rows.Count:N0} series";

    public string DismissedHeader => $"Dismissed · {Dismissed.Count:N0}";

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    /// <summary>Reads the cached lists once; the section calls this when it is opened.</summary>
    public void EnsureScanned()
    {
        if (!IsScanned)
        {
            Scan();
        }
    }

    [RelayCommand]
    private void Scan()
    {
        using var context = _contextFactory();
        var dismissedKeys = HealthDismissals.KeysFor(context, HealthDismissals.ChapterAlignment);

        Rows.Clear();
        foreach (var row in ChapterAlignmentScanner.ReadCached(context).Where(r => !dismissedKeys.Contains(r.DismissalKey)))
        {
            Rows.Add(row);
        }

        Dismissed.Clear();
        foreach (var row in HealthDismissals.RowsFor(context, HealthDismissals.ChapterAlignment))
        {
            Dismissed.Add(new DismissedFindingRow(row.Key, row.Label ?? row.Key));
        }

        IsScanned = true;
        NotifyCounts();
    }

    /// <summary>Fetches MangaDex's chapter list for every linked series that has none yet (or an old one), then rereads the lists.</summary>
    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        _checking = new CancellationTokenSource();
        StatusText = "Checking MangaDex…";
        try
        {
            var progress = new InlineProgress(p => StatusText = $"Checking MangaDex… {p.Done:N0} of {p.Total:N0}");
            var result = await ChapterAlignmentScanner.RefreshAsync(_contextFactory, _client, _nowUtc(), progress, _checking.Token);
            StatusText = result.Total == 0
                ? "Every linked series is already up to date."
                : result.StoppedEarly
                    ? $"MangaDex stopped answering after {result.Fetched:N0} of {result.Total:N0}; check again later."
                    : $"Checked {result.Fetched:N0} series.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        finally
        {
            IsChecking = false;
            _checking.Dispose();
            _checking = null;
        }

        Scan();
    }

    [RelayCommand]
    private void CancelCheck() => _checking?.Cancel();

    [RelayCommand]
    private void Open(AlignmentRow? row)
    {
        if (row is not null)
        {
            OpenSeries?.Invoke(row.SeriesId);
        }
    }

    /// <summary>"Not a problem": remembers the dismissal now, takes the row out one tick later (its own button is still raising the click).</summary>
    [RelayCommand]
    private void NotAProblem(AlignmentRow? row)
    {
        if (row is null)
        {
            return;
        }

        string label = $"{row.SeriesName} - {row.Summary}";
        using (var context = _contextFactory())
        {
            HealthDismissals.Dismiss(context, HealthDismissals.ChapterAlignment, row.DismissalKey, label);
        }

        _defer(() =>
        {
            Rows.Remove(row);
            Dismissed.Insert(0, new DismissedFindingRow(row.DismissalKey, label));
            NotifyCounts();
        });
    }

    [RelayCommand]
    private void Restore(DismissedFindingRow? row)
    {
        if (row is null)
        {
            return;
        }

        using (var context = _contextFactory())
        {
            HealthDismissals.Restore(context, HealthDismissals.ChapterAlignment, row.Key);
        }

        _defer(Scan);
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasDismissed));
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(HeaderDetail));
        OnPropertyChanged(nameof(DismissedHeader));
    }

    /// <summary>Reports on the caller's thread at once. <see cref="Progress{T}"/> posts to the captured context, so its last report could land after the final status text.</summary>
    private sealed class InlineProgress : IProgress<(int Done, int Total)>
    {
        private readonly Action<(int Done, int Total)> _report;

        public InlineProgress(Action<(int Done, int Total)> report) => _report = report;

        public void Report((int Done, int Total) value) => _report(value);
    }
}
