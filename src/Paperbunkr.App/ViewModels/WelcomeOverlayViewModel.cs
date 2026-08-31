using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Drives the first-run welcome screen (docs/superpowers/specs/2026-08-31-first-run-onboarding-
/// design.md) - three equal setup paths (add a comic folder, add a book folder, import from
/// ComicRack CE) plus Skip, replacing the old CE-migration-only auto-launch. Deliberately mirrors
/// <see cref="MigrationOverlayViewModel"/>'s shape: small, uses <see cref="PaperbunkrDb.CreateContext"/>
/// directly rather than an injected context factory (unlike <see cref="PreferencesScreenViewModel"/>,
/// which needs that seam for its own heavier test surface).
/// </summary>
public partial class WelcomeOverlayViewModel : ViewModelBase
{
    private readonly IFilePickerService _filePicker;
    private readonly Action _reloadFolderWatch;
    private readonly Action _openMigrationOverlay;
    private readonly Action _requestClose;

    public WelcomeOverlayViewModel(IFilePickerService filePicker, Action reloadFolderWatch, Action openMigrationOverlay, Action requestClose)
    {
        _filePicker = filePicker;
        _reloadFolderWatch = reloadFolderWatch;
        _openMigrationOverlay = openMigrationOverlay;
        _requestClose = requestClose;
    }

    /// <summary>Set by <see cref="MainViewModel.OpenWelcomeOverlay"/> each time the screen opens, from
    /// the same <c>File.Exists(MigrationViewModel.GetDefaultCePath())</c> check <c>App.axaml.cs</c>
    /// already computes at startup - badges the "Import from ComicRack CE" card instead of driving
    /// an auto-launch decision the way it used to.</summary>
    [ObservableProperty]
    private bool _ceInstallDetected;

    [RelayCommand]
    private async Task AddComicFolder()
    {
        string? path = await _filePicker.PickFolderAsync("Add Comic Library Folder");
        if (path is null)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            if (!context.WatchedFolders.Any(w => w.Path == path))
            {
                context.WatchedFolders.Add(new WatchedFolder { Path = path });
                context.SaveChanges();
            }
        }

        _reloadFolderWatch();
        _requestClose();
    }

    [RelayCommand]
    private async Task AddBookFolder()
    {
        string? path = await _filePicker.PickFolderAsync("Add Book Folder");
        if (path is null)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            if (!context.BookFolders.Any(f => f.Path == path))
            {
                context.BookFolders.Add(new BookFolder { Path = path });
                context.SaveChanges();
            }
        }

        _requestClose();
    }

    [RelayCommand]
    private void ImportFromCe()
    {
        _openMigrationOverlay();
        _requestClose();
    }

    [RelayCommand]
    private void Skip() => _requestClose();
}
