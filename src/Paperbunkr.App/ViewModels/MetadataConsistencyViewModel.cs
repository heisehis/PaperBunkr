using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>One series + check with its outlying issues, as a Library Health row.</summary>
public sealed class ConsistencyFindingRow
{
    public ConsistencyFindingRow(ConsistencyFinding finding)
    {
        Finding = finding;
        Outliers = finding.Outliers.Select(o => new ConsistencyOutlierRow(o.IssueId, $"#{o.Number} has {o.Value}")).ToList();
    }

    public ConsistencyFinding Finding { get; }

    public string SeriesName => Finding.SeriesName;

    public string CheckLabel => Finding.Check switch
    {
        ConsistencyCheck.Year => "Year",
        ConsistencyCheck.Publisher => "Publisher",
        _ => "Age rating",
    };

    /// <summary>What most of the series has, in words.</summary>
    public string ExpectedLabel => Finding.Check == ConsistencyCheck.Year ? $"Most issues are around {Finding.Expected}" : $"Most issues have {Finding.Expected}";

    public IReadOnlyList<ConsistencyOutlierRow> Outliers { get; }
}

/// <summary>One outlying issue: what it has, and the issue to open in the editor.</summary>
public sealed record ConsistencyOutlierRow(int IssueId, string Label);

/// <summary>
/// Library Health's "Metadata consistency" section (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.4): issues whose year,
/// publisher or age rating disagrees with the rest of their series. Report-only - each outlier opens in the issue editor. Results are
/// held for the session and recomputed on Rescan. "This is intended" dismisses one series + check.
/// </summary>
public sealed partial class MetadataConsistencyViewModel : ObservableObject
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<Action> _defer;

    public MetadataConsistencyViewModel(Func<PaperbunkrDbContext>? contextFactory = null, Action<Action>? defer = null)
    {
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _defer = defer ?? (action => Dispatcher.UIThread.Post(action));
    }

    /// <summary>Opens a series' Detail page. Set by the shell.</summary>
    public Action<int>? OpenSeries { get; set; }

    /// <summary>Opens the issue editor on one issue. Set by the shell.</summary>
    public Action<int>? OpenIssueEditor { get; set; }

    public ObservableCollection<ConsistencyFindingRow> Rows { get; } = new();

    public ObservableCollection<DismissedFindingRow> Dismissed { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderDetail))]
    private bool _isScanned;

    public bool HasRows => Rows.Count > 0;

    public bool HasDismissed => Dismissed.Count > 0;

    public bool IsClean => IsScanned && Rows.Count == 0;

    public string HeaderDetail => !IsScanned
        ? "· Not scanned yet"
        : Rows.Count == 0 ? "· Nothing inconsistent" : $"· {Rows.Count:N0} to check";

    public string DismissedHeader => $"Dismissed · {Dismissed.Count:N0}";

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
        var dismissedKeys = HealthDismissals.KeysFor(context, HealthDismissals.Consistency);

        Rows.Clear();
        foreach (var finding in MetadataConsistencyResolver.Scan(context).Where(f => !dismissedKeys.Contains(f.DismissalKey)))
        {
            Rows.Add(new ConsistencyFindingRow(finding));
        }

        Dismissed.Clear();
        foreach (var row in HealthDismissals.RowsFor(context, HealthDismissals.Consistency))
        {
            Dismissed.Add(new DismissedFindingRow(row.Key, row.Label ?? row.Key));
        }

        IsScanned = true;
        NotifyCounts();
    }

    [RelayCommand]
    private void Open(ConsistencyFindingRow? row)
    {
        if (row is not null)
        {
            OpenSeries?.Invoke(row.Finding.SeriesId);
        }
    }

    [RelayCommand]
    private void EditIssue(ConsistencyOutlierRow? outlier)
    {
        if (outlier is not null)
        {
            OpenIssueEditor?.Invoke(outlier.IssueId);
        }
    }

    /// <summary>"This is intended": remembers the dismissal now, removes the row one tick later (its own button is still raising the click).</summary>
    [RelayCommand]
    private void Intended(ConsistencyFindingRow? row)
    {
        if (row is null)
        {
            return;
        }

        string label = $"{row.SeriesName} - {row.CheckLabel.ToLowerInvariant()}";
        using (var context = _contextFactory())
        {
            HealthDismissals.Dismiss(context, HealthDismissals.Consistency, row.Finding.DismissalKey, label);
        }

        _defer(() =>
        {
            Rows.Remove(row);
            Dismissed.Insert(0, new DismissedFindingRow(row.Finding.DismissalKey, label));
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
            HealthDismissals.Restore(context, HealthDismissals.Consistency, row.Key);
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
}
