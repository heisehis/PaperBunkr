using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The reader's slide-in info panel (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #28): a read-only view of the open issue's metadata. The summary is collapsed until asked for
/// (a summary can spoil), unless the Preferences default says otherwise. Owned by <see cref="ReaderScreenViewModel"/>, which fills it on load.
/// </summary>
public sealed partial class ReaderInfoPanelViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModel))]
    private ReaderInfoModel? _model;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The summary is showing. Starts as the Preferences default for each issue opened; "Show summary" sets it for the rest of the visit to this issue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSummaryCollapsed))]
    private bool _isSummaryExpanded;

    public bool HasModel => Model is not null;

    /// <summary>There is a summary and it is still hidden: the "Show summary" button shows.</summary>
    public bool IsSummaryCollapsed => Model is { HasSummary: true } && !IsSummaryExpanded;

    /// <summary>Replaces what the panel shows (a fresh issue) and applies the summary default.</summary>
    public void Set(ReaderInfoModel? model, bool showSummaryByDefault)
    {
        Model = model;
        IsSummaryExpanded = showSummaryByDefault;
        OnPropertyChanged(nameof(IsSummaryCollapsed));
    }

    [RelayCommand]
    private void ShowSummary() => IsSummaryExpanded = true;

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    public void Close() => IsOpen = false;
}
