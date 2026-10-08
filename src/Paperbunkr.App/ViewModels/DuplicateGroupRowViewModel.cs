using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One Needs Review "Duplicate Files" group (docs/superpowers/specs/2026-09-05-duplicate-files-
/// review-design.md) - a cluster of issues <c>SmartListQueryBuilder.BuildDuplicateGroups</c> flagged
/// as the same book. <paramref name="members"/> arrives pre-sorted by that method (best candidate
/// first), so the first candidate defaults to "keep." Two actions: Resolve deletes every non-kept
/// candidate; Dismiss acknowledges the whole cluster without touching any file.
/// </summary>
public partial class DuplicateGroupRowViewModel : ViewModelBase
{
    private readonly Action<DuplicateGroupRowViewModel> _onResolve;
    private readonly Action<DuplicateGroupRowViewModel> _onDismiss;
    private readonly Action<DuplicateGroupRowViewModel>? _onResolveKeepFiles;
    private readonly Action<DuplicateGroupRowViewModel>? _onCompare;

    public DuplicateGroupRowViewModel(
        string groupLabel,
        IReadOnlyList<Issue> members,
        Action<DuplicateGroupRowViewModel> onResolve,
        Action<DuplicateGroupRowViewModel> onDismiss,
        Action<DuplicateGroupRowViewModel>? onResolveKeepFiles = null,
        Action<DuplicateGroupRowViewModel>? onCompare = null)
    {
        _onCompare = onCompare;
        GroupLabel = groupLabel;
        _onResolve = onResolve;
        _onDismiss = onDismiss;
        _onResolveKeepFiles = onResolveKeepFiles;

        string groupKey = Guid.NewGuid().ToString();
        // Which copy to keep is advice shown as a badge (smart features §5.2); the default selection below is unchanged.
        var keeper = Paperbunkr.Data.Metadata.DuplicateKeeperRanker.Recommend(members);
        Candidates = new ObservableCollection<DuplicateCandidateViewModel>(
            members.Select(i => new DuplicateCandidateViewModel(i, groupKey)
            {
                IsRecommended = keeper?.IssueId == i.Id,
                RecommendedReason = keeper?.IssueId == i.Id ? keeper.Reason : null,
            }));
        Candidates[0].IsKeep = true;

        IssueIds = members.Select(i => i.Id).ToList();
    }

    public string GroupLabel { get; }

    public ObservableCollection<DuplicateCandidateViewModel> Candidates { get; }

    /// <summary>Every issue id currently in this cluster, in the same order as <see cref="Candidates"/>.</summary>
    public IReadOnlyList<int> IssueIds { get; }

    /// <summary>Ids of every candidate NOT currently marked to keep - what Resolve deletes.</summary>
    public IReadOnlyList<int> NonKeptIssueIds => Candidates.Where(c => !c.IsKeep).Select(c => c.IssueId).ToList();

    [RelayCommand]
    private void Resolve() => _onResolve(this);

    /// <summary>Resolves the group by removing the other entries from the library but leaving every file where it is.</summary>
    [RelayCommand]
    private void ResolveKeepFiles() => _onResolveKeepFiles?.Invoke(this);

    public bool CanResolveKeepFiles => _onResolveKeepFiles is not null;

    /// <summary>Opens the Compare screen for the copy marked to keep against the others (docs/superpowers/specs/2026-09-26-comic-reader-compare-design.md #11).</summary>
    [RelayCommand]
    private void Compare() => _onCompare?.Invoke(this);

    public bool CanCompare => _onCompare is not null;

    [RelayCommand]
    private void Dismiss() => _onDismiss(this);
}
