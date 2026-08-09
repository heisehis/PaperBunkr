using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Backs a live progress toast (docs/superpowers/specs/2026-08-09-embedded-metadata-and-migration-relocation-design.md
/// follow-up) - shown via <see cref="MainViewModel.ShowProgressToast"/> while a long-running
/// library action (Generate Covers, Sync Metadata) runs, and closed via
/// <see cref="MainViewModel.CloseProgressToast"/> once it finishes. The toast's content control is
/// data-bound directly to an instance of this, so updating <see cref="Done"/>/<see cref="Total"/>
/// while the toast is showing updates it live - no need to re-show or replace it mid-run.
/// </summary>
public partial class ToastProgressViewModel : ViewModelBase
{
    public ToastProgressViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }

    [ObservableProperty]
    private int _done;

    [ObservableProperty]
    private int _total;

    public double Fraction => Total > 0 ? (double)Done / Total : 0;

    partial void OnDoneChanged(int value) => OnPropertyChanged(nameof(Fraction));

    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(Fraction));
}
