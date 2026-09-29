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
    private readonly Action? _onRestore;

    /// <param name="onRestore">Supplied only for a row in the "Dismissed" sub-group (docs/superpowers/specs/2026-09-28-library-health-dismissed-rows-design.md).</param>
    public EmptySeriesRowViewModel(int seriesId, string displayLabel, Action onRemove, Action onDismiss, Action? onRestore = null)
    {
        SeriesId = seriesId;
        DisplayLabel = displayLabel;
        _onDismiss = onDismiss;
        _onRestore = onRestore;
        DeleteConfirm = new TwoStepConfirm(onRemove);
    }

    public int SeriesId { get; }

    public string DisplayLabel { get; }

    public TwoStepConfirm DeleteConfirm { get; }

    /// <summary>True for a row in the "Dismissed" sub-group - shows Restore in place of Dismiss.</summary>
    public bool IsDismissed => _onRestore is not null;

    [RelayCommand]
    private void Dismiss()
    {
        DeleteConfirm.Cancel();
        _onDismiss();
    }

    [RelayCommand]
    private void Restore()
    {
        DeleteConfirm.Cancel();
        _onRestore?.Invoke();
    }
}
