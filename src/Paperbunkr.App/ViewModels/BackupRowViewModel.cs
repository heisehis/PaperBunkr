using System;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in the Preferences Advanced tab's backup list. Restore is a single-click-destructive
/// action (it overwrites the live database file) guarded by the same two-step inline confirm as
/// <see cref="MissingFileRowViewModel.Remove"/> - the button label flips to "Confirm restore?"
/// and reverts after a few seconds if not clicked again, rather than a modal dialog.
/// </summary>
public partial class BackupRowViewModel : ViewModelBase
{
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    private readonly Action<BackupRowViewModel> _onRestore;
    private DispatcherTimer? _confirmRevertTimer;

    public BackupRowViewModel(string filePath, Action<BackupRowViewModel> onRestore)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        _onRestore = onRestore;
    }

    public string FilePath { get; }

    public string FileName { get; }

    [ObservableProperty]
    private string _restoreLabel = "Restore";

    [RelayCommand]
    private void Restore()
    {
        if (RestoreLabel == "Restore")
        {
            RestoreLabel = "Confirm restore?";
            _confirmRevertTimer?.Stop();
            _confirmRevertTimer = new DispatcherTimer { Interval = ConfirmWindow };
            _confirmRevertTimer.Tick += (_, _) => CancelPendingConfirm();
            _confirmRevertTimer.Start();
            return;
        }

        CancelPendingConfirm();
        _onRestore(this);
    }

    private void CancelPendingConfirm()
    {
        _confirmRevertTimer?.Stop();
        _confirmRevertTimer = null;
        RestoreLabel = "Restore";
    }
}
