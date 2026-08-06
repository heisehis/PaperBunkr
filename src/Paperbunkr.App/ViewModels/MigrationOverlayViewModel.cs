using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

public enum MigrationOverlayMode
{
    Migrate,
    Review,
}

/// <summary>
/// Owns the migration overlay's two panels (docs/superpowers/specs/2026-08-06-migration-ux-design.md
/// §B/§C): the Locate-&gt;...-&gt;Results migration flow (<see cref="Migration"/>) and the persistent
/// Needs Review queue (<see cref="NeedsReview"/>). <see cref="Open"/> decides which one to show -
/// Review if anything is pending, Migrate (freshly reset) otherwise - so reopening after a
/// completed migration lands on outstanding review items instead of restarting at Locate.
/// </summary>
public partial class MigrationOverlayViewModel : ViewModelBase
{
    public MigrationOverlayViewModel(IFilePickerService filePicker, Action<int> onOpenSeriesDetail)
    {
        NeedsReview = new NeedsReviewViewModel(onOpenSeriesDetail, filePicker);
        Migration = new MigrationViewModel(filePicker, onCompleted: () => NeedsReview.Refresh());
    }

    public MigrationViewModel Migration { get; }

    public NeedsReviewViewModel NeedsReview { get; }

    [ObservableProperty]
    private MigrationOverlayMode _mode = MigrationOverlayMode.Migrate;

    public bool IsMigrateMode => Mode == MigrationOverlayMode.Migrate;
    public bool IsReviewMode => Mode == MigrationOverlayMode.Review;

    partial void OnModeChanged(MigrationOverlayMode value)
    {
        OnPropertyChanged(nameof(IsMigrateMode));
        OnPropertyChanged(nameof(IsReviewMode));
    }

    public void Open()
    {
        NeedsReview.Refresh();
        if (NeedsReview.HasPendingItems)
        {
            Mode = MigrationOverlayMode.Review;
        }
        else
        {
            Migration.ResetToLocate();
            Mode = MigrationOverlayMode.Migrate;
        }
    }

    [RelayCommand]
    private void SwitchToReview()
    {
        NeedsReview.Refresh();
        Mode = MigrationOverlayMode.Review;
    }

    [RelayCommand]
    private void SwitchToMigrate()
    {
        Migration.ResetToLocate();
        Mode = MigrationOverlayMode.Migrate;
    }
}
