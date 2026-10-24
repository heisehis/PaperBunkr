using System;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One field of a manga chapter that currently differs from its series on purpose (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md D13), shown in the
/// Issue Properties editor with a "Reset to series" button. Resetting puts the series value into the editor's buffer; the override itself goes away when the editor saves.
/// </summary>
public sealed partial class ChapterExceptionRowViewModel : ViewModelBase
{
    private readonly Action<ChapterExceptionRowViewModel> _reset;

    public ChapterExceptionRowViewModel(string key, string label, string seriesValueText, Action<ChapterExceptionRowViewModel> reset)
    {
        Key = key;
        Label = label;
        SeriesValueText = seriesValueText;
        _reset = reset;
    }

    public string Key { get; }

    public string Label { get; }

    /// <summary>What the series holds for this field ("(none)" is never shown: a field with no series value has no exception).</summary>
    public string SeriesValueText { get; }

    [RelayCommand]
    private void Reset() => _reset(this);
}
