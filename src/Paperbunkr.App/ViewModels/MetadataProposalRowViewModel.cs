using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One filename-inferred field proposal, shown in the Needs Review queue's "Metadata Proposals"
/// section (docs/superpowers/specs/2026-08-17-metadata-model-phase2a-metadata-proposals-design.md).
/// Same pattern as <see cref="SeriesConflictRowViewModel"/> - deliberately unaware of the underlying
/// <c>MetadataProposal</c> entity, the caller supplies display strings plus Accept/Reject
/// callbacks. Deliberately no bulk-action static helper (unlike <c>SeriesConflictRowViewModel.
/// ApplyBulkAction</c>) - proposal volume per scan is small and each is a distinct field/value
/// judgment, not a repeatable yes/no pattern, per the design doc's own scope note.
///
/// Unlike <see cref="SeriesConflictRowViewModel"/>, <see cref="IsAlreadyAccepted"/> does NOT set
/// <see cref="IsResolved"/> at construction - an Automatic-policy proposal arrives already
/// Accepted, but that's "applied, still auditable/correctable," not "done." Accept/Reject stay
/// actionable either way; <see cref="IsResolved"/> only flips once the user explicitly clicks one
/// of them this session, same as every other Needs Review row.
/// </summary>
public partial class MetadataProposalRowViewModel : ViewModelBase
{
    private readonly Action<MetadataProposalRowViewModel> _onAccept;
    private readonly Action<MetadataProposalRowViewModel> _onReject;

    public MetadataProposalRowViewModel(
        string issueLabel,
        string fieldLabel,
        string? currentValue,
        string? proposedValue,
        string sourceLabel,
        bool isAlreadyAccepted,
        Action<MetadataProposalRowViewModel> onAccept,
        Action<MetadataProposalRowViewModel> onReject)
    {
        IssueLabel = issueLabel;
        FieldLabel = fieldLabel;
        CurrentValue = string.IsNullOrEmpty(currentValue) ? "(empty)" : currentValue;
        ProposedValue = string.IsNullOrEmpty(proposedValue) ? "(empty)" : proposedValue;
        SourceLabel = sourceLabel;
        IsAlreadyAccepted = isAlreadyAccepted;
        _onAccept = onAccept;
        _onReject = onReject;
    }

    public string IssueLabel { get; }

    public string FieldLabel { get; }

    public string CurrentValue { get; }

    public string ProposedValue { get; }

    public string SourceLabel { get; }

    /// <summary>True when this row was already <c>Accepted</c> (the <c>Automatic</c> policy's default) before the user did anything this session - informational only, doesn't affect whether Accept/Reject are clickable.</summary>
    public bool IsAlreadyAccepted { get; }

    [ObservableProperty]
    private bool _isResolved;

    [ObservableProperty]
    private string? _resolutionLabel;

    [RelayCommand]
    private void Accept()
    {
        _onAccept(this);
        IsResolved = true;
        ResolutionLabel = "Accepted";
    }

    [RelayCommand]
    private void Reject()
    {
        _onReject(this);
        IsResolved = true;
        ResolutionLabel = "Rejected";
    }
}
