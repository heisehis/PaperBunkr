using System;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Owns the migration overlay's Locate-&gt;...-&gt;Results flow (docs/superpowers/specs/2026-08-06-migration-ux-design.md
/// §B). The persistent Needs Review queue that used to be this overlay's second tab now lives in Preferences →
/// Library → Library Health (docs/superpowers/specs/2026-09-25-needs-review-into-library-health-design.md); this
/// VM only holds the shared <see cref="NeedsReviewViewModel"/> so a finished migration can refresh it, and relays
/// the Results screen's "Review in Library Health" button.
/// </summary>
public partial class MigrationOverlayViewModel : ViewModelBase
{
    private readonly Action _onReviewInLibraryHealth;

    public MigrationOverlayViewModel(IFilePickerService filePicker, NeedsReviewViewModel needsReview, Action onReviewInLibraryHealth)
    {
        _onReviewInLibraryHealth = onReviewInLibraryHealth;
        Migration = new MigrationViewModel(filePicker, onCompleted: () => _ = needsReview.RefreshAsync());
    }

    public MigrationViewModel Migration { get; }

    /// <summary>Always starts a fresh Locate step - reopening after a completed migration no longer lands on review items, that queue has its own home now.</summary>
    public void Open() => Migration.ResetToLocate();

    [RelayCommand]
    private void ReviewInLibraryHealth() => _onReviewInLibraryHealth();

    [RelayCommand]
    private void SwitchToMigrate() => Migration.ResetToLocate();
}
