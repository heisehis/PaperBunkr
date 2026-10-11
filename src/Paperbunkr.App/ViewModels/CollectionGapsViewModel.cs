using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>A finding the user dismissed, listed in a section's collapsed "Dismissed" sub-group with a Restore button.</summary>
public sealed record DismissedFindingRow(string Key, string Label);

/// <summary>
/// Library Health's "Collection gaps" section (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.3): every series with holes
/// in its run of owned issue numbers, as a compact table. Recomputed when the section opens and on Rescan - the scan is one query, so
/// there is no background job and nothing cached. "Not a gap" is a <see cref="HealthDismissals"/> entry keyed on the series and its
/// missing numbers, so a new hole in the same series brings the row back.
/// </summary>
public sealed partial class CollectionGapsViewModel : ObservableObject
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<Action> _defer;

    /// <param name="defer">How a row removal is postponed until the click that asked for it has finished routing (tests pass an immediate runner).</param>
    public CollectionGapsViewModel(Func<PaperbunkrDbContext>? contextFactory = null, Action<Action>? defer = null)
    {
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _defer = defer ?? (action => Dispatcher.UIThread.Post(action));
    }

    /// <summary>Opens a series' Detail page. Set by the shell; null in tests and until then.</summary>
    public Action<int>? OpenSeries { get; set; }

    public ObservableCollection<CollectionGapRow> Rows { get; } = new();

    public ObservableCollection<DismissedFindingRow> Dismissed { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderDetail))]
    private bool _isScanned;

    public bool HasRows => Rows.Count > 0;

    public bool HasDismissed => Dismissed.Count > 0;

    /// <summary>True once a scan has run and found nothing to show.</summary>
    public bool IsClean => IsScanned && Rows.Count == 0;

    /// <summary>The one-line summary beside the section's title.</summary>
    public string HeaderDetail => !IsScanned
        ? "· Not scanned yet"
        : Rows.Count == 0 ? "· No gaps" : $"· {Rows.Count:N0} series";

    public string DismissedHeader => $"Dismissed · {Dismissed.Count:N0}";

    /// <summary>Scans once; the section calls this when it is opened.</summary>
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
        var dismissedKeys = HealthDismissals.KeysFor(context, HealthDismissals.CollectionGap);

        Rows.Clear();
        foreach (var row in CollectionGapResolver.ForLibrary(context).Where(r => !dismissedKeys.Contains(r.DismissalKey)))
        {
            Rows.Add(row);
        }

        Dismissed.Clear();
        foreach (var row in HealthDismissals.RowsFor(context, HealthDismissals.CollectionGap))
        {
            Dismissed.Add(new DismissedFindingRow(row.Key, row.Label ?? row.Key));
        }

        IsScanned = true;
        NotifyCounts();
    }

    [RelayCommand]
    private void Open(CollectionGapRow? row)
    {
        if (row is not null)
        {
            OpenSeries?.Invoke(row.SeriesId);
        }
    }

    /// <summary>"Not a gap": remembers the dismissal now, takes the row out of the list one tick later (its own button is still raising the click).</summary>
    [RelayCommand]
    private void NotAGap(CollectionGapRow? row)
    {
        if (row is null)
        {
            return;
        }

        string label = $"{row.SeriesName} - missing {row.Missing}";
        using (var context = _contextFactory())
        {
            HealthDismissals.Dismiss(context, HealthDismissals.CollectionGap, row.DismissalKey, label);
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
            HealthDismissals.Restore(context, HealthDismissals.CollectionGap, row.Key);
        }

        // The restored finding has to be recomputed (it may have changed or gone), which rebuilds both lists.
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
}
