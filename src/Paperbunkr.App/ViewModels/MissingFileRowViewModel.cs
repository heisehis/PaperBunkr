using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One Needs Review "Missing Files" row, offering three remediation actions
/// (docs/superpowers/specs/2026-08-06-migration-ux-polish-design.md §2): Relink (pick a new file),
/// Remove from library (delete the underlying <c>Issue</c> row - the app's first single-click-
/// destructive real-data delete, guarded by a two-step inline confirm rather than a modal, to
/// match the app's existing lightweight interaction style) and Dismiss (acknowledge and stop
/// asking, without touching the data).
/// </summary>
public partial class MissingFileRowViewModel : ViewModelBase
{
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    private readonly Func<MissingFileRowViewModel, Task> _onRelink;
    private readonly Action<MissingFileRowViewModel> _onRemove;
    private readonly Action<MissingFileRowViewModel> _onDismiss;
    private DispatcherTimer? _confirmRevertTimer;

    public MissingFileRowViewModel(
        int issueId,
        string displayLabel,
        Func<MissingFileRowViewModel, Task> onRelink,
        Action<MissingFileRowViewModel> onRemove,
        Action<MissingFileRowViewModel> onDismiss)
    {
        IssueId = issueId;
        DisplayLabel = displayLabel;
        _onRelink = onRelink;
        _onRemove = onRemove;
        _onDismiss = onDismiss;
    }

    public int IssueId { get; }

    public string DisplayLabel { get; }

    [ObservableProperty]
    private string _removeLabel = "Remove";

    private bool IsConfirmingRemove => RemoveLabel != "Remove";

    [RelayCommand]
    private async Task Relink()
    {
        CancelPendingConfirm();
        await _onRelink(this);
    }

    [RelayCommand]
    private void Remove()
    {
        if (!IsConfirmingRemove)
        {
            RemoveLabel = "Confirm remove?";
            _confirmRevertTimer?.Stop();
            _confirmRevertTimer = new DispatcherTimer { Interval = ConfirmWindow };
            _confirmRevertTimer.Tick += (_, _) => CancelPendingConfirm();
            _confirmRevertTimer.Start();
            return;
        }

        CancelPendingConfirm();
        _onRemove(this);
    }

    [RelayCommand]
    private void Dismiss()
    {
        CancelPendingConfirm();
        _onDismiss(this);
    }

    private void CancelPendingConfirm()
    {
        _confirmRevertTimer?.Stop();
        _confirmRevertTimer = null;
        RemoveLabel = "Remove";
    }
}
