using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in the Grouped Review overlay (docs/superpowers/specs/2026-09-05-plugin-grouped-review-
/// and-scan-alerts-design.md §3) - a plugin-supplied <c>PluginBookGroup</c> rendered with a
/// "keep this one" choice per book and a "skip this group" checkbox. "Keep" is a plain
/// <c>Button</c>/<c>Classes.active</c> toggle (this app's established selection idiom - see
/// Library's own tile checkboxes/mode-option buttons) rather than a native Avalonia
/// <c>RadioButton</c>, which nothing else in this codebase uses. Reuses <see cref="IssueCardSample.IsSelected"/>
/// (from <see cref="ISelectableCard"/>) to mean "this is the kept copy" for these specific,
/// overlay-local card instances - unrelated to Library's own multi-select use of the same flag on
/// its own, separate card instances. <c>onChanged</c> fires on either kind of change so the owning
/// <see cref="SmartScreenViewModel"/> can recompute its live delete count - same lightweight
/// constructor-callback convention <see cref="MissingFileRowViewModel"/> already uses, not a
/// custom event.
/// </summary>
public partial class PluginGroupRowViewModel : ViewModelBase
{
    private readonly System.Action _onChanged;

    public PluginGroupRowViewModel(string label, IReadOnlyList<IssueCardSample> books, int? suggestedKeepIssueId, System.Action onChanged)
    {
        Label = label;
        Books = books;
        _onChanged = onChanged;

        int keepId = suggestedKeepIssueId ?? (books.Count > 0 ? books[0].Id : 0);
        _selectedKeepIssueId = keepId;
        foreach (var book in books)
        {
            book.IsSelected = book.Id == keepId;
        }
    }

    public string Label { get; }

    public IReadOnlyList<IssueCardSample> Books { get; }

    [ObservableProperty]
    private int _selectedKeepIssueId;

    [RelayCommand]
    private void SelectKeep(IssueCardSample book)
    {
        SelectedKeepIssueId = book.Id;
        foreach (var b in Books)
        {
            b.IsSelected = b.Id == book.Id;
        }

        _onChanged();
    }

    [ObservableProperty]
    private bool _isSkipped;

    partial void OnIsSkippedChanged(bool value) => _onChanged();

    /// <summary>How many books in this group would actually be deleted right now - every book except the kept one, or zero if the whole group is skipped.</summary>
    public int DeleteCount => IsSkipped ? 0 : Books.Count - 1;
}
