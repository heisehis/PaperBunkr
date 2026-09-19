using System;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One "Empty Rows" series row (docs/superpowers/specs/2026-09-17-series-name-matching-and-empty-
/// row-cleanup-design.md) - a zero-<c>Issue</c> <c>Series</c> ("ghost" row left behind by a rename,
/// move, or manual delete). Remove (<see cref="TwoStepConfirm"/>-gated, same as
/// <see cref="MissingFileRowViewModel"/>) and Dismiss only - no Relink concept for a series with
/// nothing to relink to.
/// </summary>
public partial class EmptySeriesRowViewModel : ViewModelBase
{
    private readonly Action _onDismiss;

    public EmptySeriesRowViewModel(int seriesId, string displayLabel, Action onRemove, Action onDismiss)
    {
        SeriesId = seriesId;
        DisplayLabel = displayLabel;
        _onDismiss = onDismiss;
        DeleteConfirm = new TwoStepConfirm(onRemove);
    }

    public int SeriesId { get; }

    public string DisplayLabel { get; }

    public TwoStepConfirm DeleteConfirm { get; }

    [RelayCommand]
    private void Dismiss()
    {
        DeleteConfirm.Cancel();
        _onDismiss();
    }
}
