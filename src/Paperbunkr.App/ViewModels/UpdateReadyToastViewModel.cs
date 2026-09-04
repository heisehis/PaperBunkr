using System;
using CommunityToolkit.Mvvm.Input;
using NetSparkleUpdater;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// "Update ready - restart to apply" toast (docs/superpowers/specs/2026-09-01-auto-update-and-
/// changelog-design.md) - a dedicated toast VM carrying actions (Restart now / Later / What's New),
/// no progress bar. Restart only ever fires on explicit
/// user action - never automatic, so an update never interrupts an in-progress reading session.
/// Carries <see cref="_downloadPath"/> alongside the item because NetSparkle's
/// <c>SparkleUpdater.InstallUpdate</c> needs both - unlike Velopack's single-argument
/// <c>ApplyUpdatesAndRestart(info)</c>, NetSparkle reports the downloaded file's path via its own
/// <c>DownloadFinished</c> event rather than deriving it from the item alone.
/// </summary>
public partial class UpdateReadyToastViewModel : ViewModelBase
{
    private readonly AppCastItem _info;
    private readonly string _downloadPath;
    private readonly UpdateService _updateService;
    private readonly Action _onClose;
    private readonly Action _onWhatsNew;

    public UpdateReadyToastViewModel(AppCastItem info, string downloadPath, UpdateService updateService, Action onClose, Action onWhatsNew)
    {
        _info = info;
        _downloadPath = downloadPath;
        _updateService = updateService;
        _onClose = onClose;
        _onWhatsNew = onWhatsNew;
    }

    public string Title => "Update ready — restart to apply";

    [RelayCommand]
    private void RestartNow() => _updateService.ApplyUpdatesAndRestart(_info, _downloadPath);

    [RelayCommand]
    private void Later() => _onClose();

    [RelayCommand]
    private void WhatsNew()
    {
        _onWhatsNew();
        _onClose();
    }
}
