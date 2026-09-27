using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One bucket of Metadata Proposals of the same kind - everything one source proposed (or auto-applied) for the same
/// field, e.g. "Number · FilenameParser · 1,204" (docs/superpowers/specs/2026-09-26-library-health-subtabs-design.md).
/// Used for both the Pending list and the Applied list (<see cref="Status"/>). On a big library that is thousands of
/// proposals, so they are not listed one by one: the group shows a count and Accept / Reject for the whole bucket, and
/// only loads its rows (the latest <see cref="RowLimit"/>, shown in a virtualized list) once it is expanded.
/// </summary>
public partial class ProposalGroupViewModel : ViewModelBase
{
    /// <summary>How many rows an expanded group loads. The bulk actions always cover the whole group, not just these.</summary>
    public const int RowLimit = 500;

    private readonly Func<ProposalGroupViewModel, IReadOnlyList<MetadataProposalRowViewModel>> _loadRows;
    private readonly Action<ProposalGroupViewModel> _onAcceptAll;
    private readonly Action<ProposalGroupViewModel> _onRejectAll;
    private bool _rowsLoaded;

    public ProposalGroupViewModel(
        MetadataProposalStatus status,
        MetadataProposalField field,
        MetadataProposalSource source,
        ExternalMetadataProvider? provider,
        int count,
        Func<ProposalGroupViewModel, IReadOnlyList<MetadataProposalRowViewModel>> loadRows,
        Action<ProposalGroupViewModel> onAcceptAll,
        Action<ProposalGroupViewModel> onRejectAll)
    {
        Status = status;
        Field = field;
        Source = source;
        Provider = provider;
        _count = count;
        _loadRows = loadRows;
        _onAcceptAll = onAcceptAll;
        _onRejectAll = onRejectAll;
        Rows = new ObservableCollection<MetadataProposalRowViewModel>();
        RejectAllConfirm = new TwoStepConfirm(() => _onRejectAll(this), "Reject All", "Confirm reject all?");
        // Pending accepts change data (write tags, move issues between series) so they are two-step too; accepting an applied
        // group only marks it reviewed and is a plain click.
        AcceptAllConfirm = new TwoStepConfirm(() => _onAcceptAll(this), "Accept All", "Confirm accept all?");
    }

    /// <summary>Pending = waiting on a human; Accepted = auto-applied and not yet reviewed.</summary>
    public MetadataProposalStatus Status { get; }

    public bool IsPending => Status == MetadataProposalStatus.Pending;

    public MetadataProposalField Field { get; }

    public MetadataProposalSource Source { get; }

    /// <summary>Which linked provider produced these, for <see cref="MetadataProposalSource.MetadataProvider"/> groups; null otherwise.</summary>
    public ExternalMetadataProvider? Provider { get; }

    /// <summary>"Number · FilenameParser" / "Summary · MetadataProvider (MangaBaka)".</summary>
    public string Title => Provider is { } provider ? $"{Field} · {Source} ({provider})" : $"{Field} · {Source}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel), nameof(HiddenRowsLabel), nameof(HasHiddenRows))]
    private int _count;

    public string CountLabel => Count == 1 ? "1 proposal" : $"{Count:N0} proposals";

    public ObservableCollection<MetadataProposalRowViewModel> Rows { get; }

    /// <summary>True when the group has more proposals than its expanded list loaded.</summary>
    public bool HasHiddenRows => IsExpanded && Count > Rows.Count;

    public string HiddenRowsLabel => $"Showing the latest {Rows.Count:N0} of {Count:N0}. Accept All / Reject All cover the whole group.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenRows))]
    private bool _isExpanded;

    public TwoStepConfirm RejectAllConfirm { get; }

    /// <summary>Two-step for a pending group; the applied group's "Accept All" uses <see cref="AcceptAllCommand"/> directly.</summary>
    public TwoStepConfirm AcceptAllConfirm { get; }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
        if (IsExpanded && !_rowsLoaded)
        {
            _rowsLoaded = true;
            foreach (var row in _loadRows(this))
            {
                Rows.Add(row);
            }

            OnPropertyChanged(nameof(HasHiddenRows));
            OnPropertyChanged(nameof(HiddenRowsLabel));
        }
    }

    /// <summary>Accept every proposal in the group without a confirm step (used for applied groups: keep them and clear them from the list).</summary>
    [RelayCommand]
    private void AcceptAll() => _onAcceptAll(this);

    /// <summary>Called by a row in this group after the user resolved it on its own, so the count follows.</summary>
    public void NotifyRowResolved() => Count = Math.Max(0, Count - 1);
}
