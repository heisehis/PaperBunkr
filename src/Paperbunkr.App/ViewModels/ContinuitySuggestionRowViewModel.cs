using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in the Story Events screen's "New shared-universe suggestions" list (docs/superpowers/
/// specs/2026-09-17-storyevent-continuity-autopopulate-design.md, Phase 2). Accept/Dismiss actions.
/// </summary>
public partial class ContinuitySuggestionRowViewModel : ViewModelBase
{
    private readonly Action<ContinuitySuggestionRowViewModel> _onAccept;
    private readonly Action<ContinuitySuggestionRowViewModel> _onDismiss;

    public ContinuitySuggestionRowViewModel(ContinuitySuggestion suggestion, Action<ContinuitySuggestionRowViewModel> onAccept, Action<ContinuitySuggestionRowViewModel> onDismiss)
    {
        Suggestion = suggestion;
        _onAccept = onAccept;
        _onDismiss = onDismiss;
    }

    public ContinuitySuggestion Suggestion { get; }

    public string SeriesName => Suggestion.Series.Name;

    public string UniverseLabel => Suggestion.UniverseLabel;

    public string Reason => Suggestion.Reason;

    public bool IsStrong => Suggestion.Strength == FormatSignalStrength.Strong;

    [RelayCommand]
    private void Accept() => _onAccept(this);

    [RelayCommand]
    private void Dismiss() => _onDismiss(this);
}
