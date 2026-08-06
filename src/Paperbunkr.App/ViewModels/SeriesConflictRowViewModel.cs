using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// A bulk resolution applied to every unresolved row in a Conflicts list at once
/// (docs/superpowers/specs/2026-08-06-migration-ux-polish-design.md §3).
/// </summary>
public enum ConflictBulkAction
{
    KeepAllSeparate,
    MergeAboveNinetyPercent,
}

/// <summary>
/// One "is this the same series?" candidate row - shared between the in-flow migration Conflicts
/// stage (<see cref="MigrationViewModel"/>) and the persistent Needs Review queue
/// (<see cref="NeedsReviewViewModel"/>), per
/// docs/superpowers/specs/2026-08-06-migration-ux-design.md §C. Deliberately unaware of whether
/// its data is a pre-commit <c>SeriesConflictCandidate</c> or a persisted <c>SeriesConflict</c>
/// row - the caller supplies display strings plus Merge/Keep-Separate callbacks, the same
/// pattern <see cref="ReadingListItemRowViewModel"/> uses for its row actions.
/// </summary>
public partial class SeriesConflictRowViewModel : ViewModelBase
{
    /// <summary>Fixed threshold for "Merge All Above 90%" (docs/superpowers/specs/2026-08-06-migration-ux-polish-design.md §3) - not a slider, keeps the bulk action simple.</summary>
    public const double BulkMergeThreshold = 0.90;

    private readonly Action<SeriesConflictRowViewModel> _onMerge;
    private readonly Action<SeriesConflictRowViewModel> _onKeepSeparate;

    public SeriesConflictRowViewModel(
        string incomingName,
        string matchedName,
        double similarity,
        Action<SeriesConflictRowViewModel> onMerge,
        Action<SeriesConflictRowViewModel> onKeepSeparate)
    {
        IncomingName = incomingName;
        MatchedName = matchedName;
        Similarity = similarity;
        SimilarityLabel = $"{similarity:P0} match";
        _onMerge = onMerge;
        _onKeepSeparate = onKeepSeparate;
    }

    public string IncomingName { get; }

    public string MatchedName { get; }

    public double Similarity { get; }

    public string SimilarityLabel { get; }

    [ObservableProperty]
    private bool _isResolved;

    [ObservableProperty]
    private string? _resolutionLabel;

    [RelayCommand]
    private void Merge()
    {
        _onMerge(this);
        IsResolved = true;
        ResolutionLabel = "Merged";
    }

    [RelayCommand]
    private void KeepSeparate()
    {
        _onKeepSeparate(this);
        IsResolved = true;
        ResolutionLabel = "Kept separate";
    }

    /// <summary>
    /// Applies <paramref name="action"/> to every currently-unresolved row in <paramref name="rows"/>,
    /// invoking each row's own Merge/KeepSeparate command so the resolution goes through the same
    /// path (and callback) a manual click would - callers don't need a separate bulk-resolution
    /// code path. Shared by <see cref="MigrationViewModel"/> (in-memory candidates) and
    /// <see cref="NeedsReviewViewModel"/> (persisted rows); each wires its own Merge/KeepSeparate
    /// callbacks per-row, so this helper only decides which rows to touch, not how a resolution is
    /// persisted.
    /// </summary>
    public static void ApplyBulkAction(IEnumerable<SeriesConflictRowViewModel> rows, ConflictBulkAction action)
    {
        foreach (var row in rows.Where(r => !r.IsResolved).ToList())
        {
            switch (action)
            {
                case ConflictBulkAction.KeepAllSeparate:
                    row.KeepSeparateCommand.Execute(null);
                    break;
                case ConflictBulkAction.MergeAboveNinetyPercent:
                    if (row.Similarity >= BulkMergeThreshold)
                    {
                        row.MergeCommand.Execute(null);
                    }

                    break;
            }
        }
    }
}
