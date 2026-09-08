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

        DisplayDate = File.GetLastWriteTime(filePath).ToString("MMM d, yyyy — h:mm tt");
        DisplaySize = FormatBytes(new FileInfo(filePath).Length);
    }

    public string FilePath { get; }

    public string FileName { get; }

    /// <summary>docs/superpowers/specs/2026-09-08-advanced-backup-file-association-redesign-design.md §3 - the backup file's own last-write time, not re-derived from its filename's embedded timestamp.</summary>
    public string DisplayDate { get; }

    public string DisplaySize { get; }

    [ObservableProperty]
    private string _restoreLabel = "Restore";

    /// <summary>Drives the Restore button's danger-armed styling while a confirm click is pending - a parallel flag to <see cref="RestoreLabel"/>, not a replacement for its text.</summary>
    [ObservableProperty]
    private bool _isArmed;

    [RelayCommand]
    private void Restore()
    {
        if (RestoreLabel == "Restore")
        {
            RestoreLabel = "Confirm restore?";
            IsArmed = true;
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
        IsArmed = false;
    }

    /// <summary>Same MB/GB-precision shape as StatusBarViewModel.FormatBytes - duplicated locally rather than extracted into a shared utility, since that file has nothing else to do with Backup Manager.</summary>
    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        double gb = bytes / 1024d / 1024d / 1024d;
        if (gb >= 1)
        {
            return $"{gb:0.0} GB";
        }

        double mb = bytes / 1024d / 1024d;
        return $"{mb:0} MB";
    }
}
