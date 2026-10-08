using System;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row of the Continuity screen's "Shared characters" suggestions (docs/superpowers/specs/2026-10-06-smart-features-design.md §7.1):
/// a series that shares several characters with a continuity's members. Accept adds it; Dismiss remembers not to suggest it again.
/// </summary>
public partial class CharacterSuggestionRowViewModel : ViewModelBase
{
    private readonly Action<CharacterSuggestionRowViewModel> _onAccept;
    private readonly Action<CharacterSuggestionRowViewModel> _onDismiss;

    public CharacterSuggestionRowViewModel(
        ContinuityCharacterSuggestion suggestion, Action<CharacterSuggestionRowViewModel> onAccept, Action<CharacterSuggestionRowViewModel> onDismiss)
    {
        Suggestion = suggestion;
        _onAccept = onAccept;
        _onDismiss = onDismiss;
    }

    public ContinuityCharacterSuggestion Suggestion { get; }

    public string SeriesName => Suggestion.SeriesName;

    public string ContinuityName => Suggestion.ContinuityName;

    public string Reason => Suggestion.Reason;

    [RelayCommand]
    private void Accept() => _onAccept(this);

    [RelayCommand]
    private void Dismiss() => _onDismiss(this);
}
