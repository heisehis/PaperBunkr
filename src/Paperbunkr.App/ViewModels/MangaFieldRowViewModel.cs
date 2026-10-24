using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One inheritable field in the Edit Series overlay (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §6): a label, the editable text, an optional
/// "chapters disagree" note, and a lock marker for a value the user set by hand that providers may not overwrite.
/// </summary>
public sealed partial class MangaFieldRowViewModel : ViewModelBase
{
    private readonly Action<MangaFieldRowViewModel>? _unlock;

    public MangaFieldRowViewModel(MangaField field, string text, string? reviewNote, bool isLocked, Action<MangaFieldRowViewModel>? unlock = null)
    {
        Field = field;
        _text = text;
        OriginalText = text;
        ReviewNote = reviewNote;
        _isLocked = isLocked;
        _unlock = unlock;
    }

    public MangaField Field { get; }

    public string Key => Field.Key;

    public string Label => Field.Label;

    public string OriginalText { get; }

    /// <summary>What a person types: names for a credit, comma-separated values for a genre or tag list.</summary>
    [ObservableProperty]
    private string _text;

    /// <summary>Names to offer while typing (the library's own publishers, people, ratings...), filled in once the library has been read.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _suggestions = Array.Empty<string>();

    /// <summary>Credits take several names separated by commas; the suggestion applies to the name being typed.</summary>
    public bool IsMultiValue => Field.Kind == MangaFieldKind.Credit;

    public string Hint => Field.Kind switch
    {
        MangaFieldKind.Credit => "Names, separated by commas",
        MangaFieldKind.Tags => "Separated by commas",
        _ => string.Empty,
    };

    /// <summary>"Chapters disagree: A · B." shown when the chapters held different values and the most common one was proposed.</summary>
    public string? ReviewNote { get; }

    public bool HasReviewNote => !string.IsNullOrEmpty(ReviewNote);

    /// <summary>Set by hand earlier, so a provider refresh will not overwrite it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockTooltip))]
    private bool _isLocked;

    public string LockTooltip => IsLocked ? "Set by you. A provider refresh will not change it. Click to let providers update it again." : string.Empty;

    [RelayCommand]
    private void Unlock()
    {
        IsLocked = false;
        _unlock?.Invoke(this);
    }
}
