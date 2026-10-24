using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in the Story Events screen's "New story-event suggestions" list (docs/superpowers/specs/
/// 2026-09-17-storyevent-continuity-autopopulate-design.md, Phase 1) - a whole not-yet-created
/// <see cref="StoryEventCandidate"/>, not a single issue like <see cref="SuggestedIssueRowViewModel"/>.
/// Accept/Dismiss actions.
/// </summary>
public partial class StoryEventCandidateRowViewModel : ViewModelBase
{
    private readonly Func<StoryEventCandidateRowViewModel, Task> _onAccept;
    private readonly Action<StoryEventCandidateRowViewModel> _onDismiss;
    private readonly Action? _onStateChanged;

    public StoryEventCandidateRowViewModel(StoryEventCandidate candidate, Func<StoryEventCandidateRowViewModel, Task> onAccept, Action<StoryEventCandidateRowViewModel> onDismiss, Action? onStateChanged = null)
    {
        Candidate = candidate;
        _onAccept = onAccept;
        _onDismiss = onDismiss;
        _onStateChanged = onStateChanged;
    }

    public StoryEventCandidate Candidate { get; }

    public string ArcName => Candidate.ArcName;

    public string Publisher => Candidate.Publisher;

    public string Reason => Candidate.Reason;

    public bool IsStrong => Candidate.Strength == FormatSignalStrength.Strong;

    public int MemberCount => Candidate.Members.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    private bool _isAccepting;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string? _batchError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    private bool _isQueueBusy;

    public bool CanAct => !IsAccepting && !IsQueueBusy;

    partial void OnIsSelectedChanged(bool value) => _onStateChanged?.Invoke();
    partial void OnIsAcceptingChanged(bool value) => _onStateChanged?.Invoke();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task Accept()
    {
        IsAccepting = true;
        try
        {
            await _onAccept(this);
        }
        finally
        {
            IsAccepting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Dismiss() => _onDismiss(this);
}
