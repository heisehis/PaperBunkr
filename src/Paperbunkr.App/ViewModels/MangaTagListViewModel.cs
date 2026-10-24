using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One weighted tag list in Edit Series (genres, or tags): a row per value with its category and how central it is (none / Incidental / Recurrent / Defining / Core), a box to add
/// values (a pasted comma list adds several), a remove button per row, and the lock marker when the list was set by hand
/// (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §14). The same weights pass down to every chapter without an override.
/// </summary>
public sealed partial class MangaTagListViewModel : ViewModelBase
{
    private readonly Action<string> _unlock;

    public MangaTagListViewModel(string key, string label, string defaultCategory, Action<string> unlock)
    {
        Key = key;
        Label = label;
        DefaultCategory = defaultCategory;
        _unlock = unlock;
    }

    public string Key { get; }

    public string Label { get; }

    public string DefaultCategory { get; }

    /// <summary>Values to offer in the add box (the library's own genres or tags, plus a starter set).</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _suggestions = Array.Empty<string>();

    /// <summary>Categories to offer in each row's category box.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _categorySuggestions = Array.Empty<string>();

    public ObservableCollection<TagEditRowViewModel> Rows { get; } = new();

    public bool HasRows => Rows.Count > 0;

    /// <summary>Typed or pasted values to add; several may be separated by commas.</summary>
    [ObservableProperty]
    private string _addText = string.Empty;

    [ObservableProperty]
    private string? _reviewNote;

    public bool HasReviewNote => !string.IsNullOrEmpty(ReviewNote);

    partial void OnReviewNoteChanged(string? value) => OnPropertyChanged(nameof(HasReviewNote));

    /// <summary>The list was set by hand, so a provider refresh will not change it.</summary>
    [ObservableProperty]
    private bool _isLocked;

    public void Load(IEnumerable<MangaTagValue> tags, bool isLocked, string? reviewNote)
    {
        Rows.Clear();
        foreach (var tag in tags)
        {
            Rows.Add(new TagEditRowViewModel(tag.Value, tag.Category, tag.Weight));
        }

        AddText = string.Empty;
        IsLocked = isLocked;
        ReviewNote = reviewNote;
        OnPropertyChanged(nameof(HasRows));
    }

    /// <summary>The list as the service saves it. An empty category falls back to the list's default.</summary>
    public List<MangaTagValue> ToValues() =>
        Rows.Select(r => new MangaTagValue(r.Value, string.IsNullOrWhiteSpace(r.Category) ? DefaultCategory : r.Category.Trim(), r.Weight)).ToList();

    /// <summary>Everything a person can change in the list, as one string, so the editor can tell whether anything is unsaved.</summary>
    public string Signature => string.Join('␞', Rows.Select(r => $"{r.Value}␟{r.Category}␟{(int)r.Weight}")) + "␝" + AddText;

    [RelayCommand]
    private void Add()
    {
        foreach (string name in CreatorResolver.ParseNames(AddText))
        {
            if (!Rows.Any(r => string.Equals(r.Value, name, StringComparison.OrdinalIgnoreCase)))
            {
                Rows.Add(new TagEditRowViewModel(name, DefaultCategory, IssueTagWeight.Unset));
            }
        }

        AddText = string.Empty;
        OnPropertyChanged(nameof(HasRows));
    }

    [RelayCommand]
    private void Remove(TagEditRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        // Not removed inline: this runs from the row's own button, and removing a row from inside its own click detaches the control mid-event.
        Dispatcher.UIThread.Post(() =>
        {
            Rows.Remove(row);
            OnPropertyChanged(nameof(HasRows));
        });
    }

    [RelayCommand]
    private void Unlock()
    {
        IsLocked = false;
        _unlock(Key);
    }
}
