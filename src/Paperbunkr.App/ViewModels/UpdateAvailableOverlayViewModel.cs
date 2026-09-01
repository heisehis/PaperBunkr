using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSparkleUpdater;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Update-available prompt (docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md) -
/// ask-before-download, matching CE's own dialog shape
/// (_reference/ComicRackCE/ComicRack/MainForm.cs:4529-4544): version + changelog excerpt, Download /
/// Not now, and a persisted "don't check on startup" checkbox. Mirrors
/// <see cref="WelcomeOverlayViewModel"/>'s shape - small, uses <see cref="PaperbunkrDb.CreateContext"/>
/// directly for the one setting it writes, rather than an injected context factory. Carries a single
/// <see cref="AppCastItem"/> (not the whole <see cref="UpdateInfo"/> - NetSparkle's own multi-item
/// wrapper) since <see cref="MainViewModel"/> already picks the one candidate before showing this.
/// </summary>
public partial class UpdateAvailableOverlayViewModel : ViewModelBase
{
    private readonly Func<AppCastItem, Task> _onDownload;
    private readonly Action _requestClose;

    public UpdateAvailableOverlayViewModel(Func<AppCastItem, Task> onDownload, Action requestClose)
    {
        _onDownload = onDownload;
        _requestClose = requestClose;
    }

    [ObservableProperty]
    private AppCastItem? _info;

    [ObservableProperty]
    private string? _changelogBody;

    public string VersionText => Info is null ? string.Empty : $"v{Info.Version}";

    /// <summary>Set by <see cref="MainViewModel"/>'s startup check right before opening this overlay.</summary>
    public void Show(AppCastItem info, string? changelogBody)
    {
        Info = info;
        ChangelogBody = changelogBody;
        OnPropertyChanged(nameof(VersionText));
    }

    [RelayCommand]
    private async Task Download()
    {
        if (Info is not null)
        {
            await _onDownload(Info);
        }

        _requestClose();
    }

    [RelayCommand]
    private void NotNow() => _requestClose();

    [ObservableProperty]
    private bool _dontCheckOnStartup;

    partial void OnDontCheckOnStartupChanged(bool value)
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.CheckForUpdatesOnStartup = !value;
        context.SaveChanges();
    }
}
