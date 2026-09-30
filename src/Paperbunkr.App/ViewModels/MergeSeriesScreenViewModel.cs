using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>One radio row of the Merge series dialog.</summary>
public sealed partial class MergeSeriesCandidate : ObservableObject
{
    public required int SeriesId { get; init; }

    public required string Name { get; init; }

    public required string Detail { get; init; }

    public required int IssueCount { get; init; }

    public IBrush? CoverBrush { get; init; }

    [ObservableProperty]
    private bool _isTarget;
}

/// <summary>
/// The Merge series dialog (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §3) - the first merge flow where the user picks
/// which series survives; the Needs Review and Find Similar Series flows pick it themselves. The default is the series with the most issues,
/// as Find Similar Series does. The duplicate warning uses <c>SeriesMergeHelper</c>'s own <c>(EffectiveNumber, EffectiveVolume)</c> key.
/// </summary>
public sealed partial class MergeSeriesScreenViewModel : ViewModelBase
{
    private readonly Action _close;
    private readonly Action<int, IReadOnlyList<int>> _merge;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private Dictionary<int, HashSet<(string?, string?)>> _keysBySeries = new();

    public MergeSeriesScreenViewModel(Action close, Action<int, IReadOnlyList<int>> merge)
        : this(close, merge, PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test seam - production uses the real per-user database.</summary>
    internal MergeSeriesScreenViewModel(Action close, Action<int, IReadOnlyList<int>> merge, Func<PaperbunkrDbContext> contextFactory)
    {
        _close = close;
        _merge = merge;
        _contextFactory = contextFactory;
    }

    public ObservableCollection<MergeSeriesCandidate> Candidates { get; } = new();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private int _duplicateCount;

    public MergeSeriesCandidate? Target => Candidates.FirstOrDefault(c => c.IsTarget);

    public string MergeLabel => Target is { } t ? $"Merge into {t.Name}" : "Merge";

    public bool HasDuplicates => DuplicateCount > 0;

    public string DuplicateWarning => DuplicateCount == 1
        ? "1 issue exists in both. The duplicate row is removed from the library; the file on disk is not touched. This can't be undone."
        : $"{DuplicateCount} issues exist in both. The duplicate rows are removed from the library; the files on disk are not touched. This can't be undone.";

    public string NoDuplicateNote => "No issue exists in more than one of these series. This can't be undone.";

    /// <summary>Fills the rows for <paramref name="seriesIds"/>; <paramref name="covers"/> supplies each card's cover brush when the Library has one.</summary>
    public void Load(IReadOnlyList<int> seriesIds, IReadOnlyDictionary<int, IBrush>? covers = null)
    {
        foreach (var candidate in Candidates)
        {
            candidate.PropertyChanged -= OnCandidateChanged;
        }

        Candidates.Clear();
        using var context = _contextFactory();
        var series = context.Series.Where(s => seriesIds.Contains(s.Id)).ToList();
        var issues = context.Issues.Where(i => seriesIds.Contains(i.SeriesId)).ToList();
        _keysBySeries = issues.GroupBy(i => i.SeriesId)
            .ToDictionary(g => g.Key, g => g.Select(i => (i.EffectiveNumber(), i.EffectiveVolume())).ToHashSet());

        foreach (var s in series.OrderByDescending(s => _keysBySeries.GetValueOrDefault(s.Id)?.Count ?? 0).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            int count = issues.Count(i => i.SeriesId == s.Id);
            string detail = string.Join(" · ", new[] { s.Publisher, count == 1 ? "1 issue" : $"{count} issues" }.Where(p => !string.IsNullOrWhiteSpace(p)));
            var candidate = new MergeSeriesCandidate
            {
                SeriesId = s.Id,
                Name = s.Name,
                Detail = detail,
                IssueCount = count,
                CoverBrush = covers?.GetValueOrDefault(s.Id) ?? SeriesCardSample.CoverBrushFor(s.Name),
            };
            candidate.PropertyChanged += OnCandidateChanged;
            Candidates.Add(candidate);
        }

        Title = Candidates.Count == 1 ? "Merge series" : $"Merge {Candidates.Count} series";
        if (Candidates.FirstOrDefault() is { } first)
        {
            first.IsTarget = true;
        }

        Recompute();
    }

    [RelayCommand]
    private void ChooseTarget(MergeSeriesCandidate candidate)
    {
        foreach (var c in Candidates)
        {
            c.IsTarget = ReferenceEquals(c, candidate);
        }
    }

    private void OnCandidateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MergeSeriesCandidate.IsTarget) && sender is MergeSeriesCandidate { IsTarget: true } chosen)
        {
            foreach (var c in Candidates.Where(c => !ReferenceEquals(c, chosen)))
            {
                c.IsTarget = false;
            }

            Recompute();
        }
    }

    /// <summary>Rows the merge would remove: a source issue whose key the target - or an earlier source, merged first - already has.</summary>
    private void Recompute()
    {
        int duplicates = 0;
        if (Target is { } target)
        {
            var seen = new HashSet<(string?, string?)>(_keysBySeries.GetValueOrDefault(target.SeriesId) ?? new());
            foreach (var source in Candidates.Where(c => !ReferenceEquals(c, target)))
            {
                foreach (var key in _keysBySeries.GetValueOrDefault(source.SeriesId) ?? new())
                {
                    if (!seen.Add(key))
                    {
                        duplicates++;
                    }
                }
            }
        }

        DuplicateCount = duplicates;
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(MergeLabel));
        OnPropertyChanged(nameof(HasDuplicates));
        OnPropertyChanged(nameof(DuplicateWarning));
        MergeCommand.NotifyCanExecuteChanged();
    }

    private bool CanMerge() => Target is not null && Candidates.Count > 1;

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private void Merge()
    {
        if (Target is not { } target)
        {
            return;
        }

        var sources = Candidates.Where(c => !ReferenceEquals(c, target)).Select(c => c.SeriesId).ToList();
        int targetId = target.SeriesId;

        // Deferred: the merge reloads the Library and the close detaches this button while its click is still routing.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _close();
            _merge(targetId, sources);
        });
    }

    [RelayCommand]
    private void Cancel() => Avalonia.Threading.Dispatcher.UIThread.Post(_close);
}
