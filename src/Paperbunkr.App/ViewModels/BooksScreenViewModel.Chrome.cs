using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>The Books screen's header, toolbar labels and selection bar (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §3.1-3.2).</summary>
public partial class BooksScreenViewModel
{
    /// <summary>"214 books · 3 reading" under nothing else: the header's one caption.</summary>
    public string HeaderSummary
    {
        get
        {
            int total = _allCards.Count(c => !c.IsGhost);
            int reading = _allCards.Count(c => c.Status == BookReadingStatus.Reading);
            string books = total == 1 ? "1 book" : string.Create(CultureInfo.CurrentCulture, $"{total:N0} books");
            return reading > 0 ? $"{books} · {reading} reading" : books;
        }
    }

    /// <summary>"Group", or "Group: Series" once grouping is on: the value shows only when it is not the default.</summary>
    public string GroupButtonLabel => GroupField == BooksGroupField.None ? "Group" : $"Group: {GroupLabel}";

    /// <summary>"Sort", or "Sort: Recently added" once the order is not plain Title A-Z.</summary>
    public string SortButtonLabel =>
        SortField == BooksSortField.Title && SortDirection == SortDirection.Ascending ? "Sort" : $"Sort: {SortLabel}";

    /// <summary>"of 214" beside the selection count.</summary>
    public string SelectionOfLabel => string.Create(CultureInfo.CurrentCulture, $"of {_allCards.Count(c => !c.IsGhost):N0}");

    /// <summary>Raised by <see cref="Rebuild"/>: every load, sort, group, tab and filter change goes through it.</summary>
    private void RaiseChromeLabels()
    {
        OnPropertyChanged(nameof(HeaderSummary));
        OnPropertyChanged(nameof(GroupButtonLabel));
        OnPropertyChanged(nameof(SortButtonLabel));
        OnPropertyChanged(nameof(SelectionOfLabel));
    }

    /// <summary>Selection bar "Set status ▸ {status}" for every selected book.</summary>
    [RelayCommand]
    private void SetSelectionStatus(BookReadingStatus status)
    {
        var ids = Selection.SelectedIds.ToList();
        ActiveDropdown = null;
        if (ids.Count > 0 && new BookQuickEditService().SetStatus(ids, status))
        {
            // The menu row raising this lives in a popup over the cards a reload rebuilds: reload after the click has finished routing.
            Dispatcher.UIThread.Post(LoadFromDatabase);
        }
    }

    /// <summary>Selection bar "Rate ▸ {stars}" for every selected book; null clears.</summary>
    [RelayCommand]
    private void RateSelection(float? rating)
    {
        var ids = Selection.SelectedIds.ToList();
        ActiveDropdown = null;
        if (ids.Count > 0 && new BookQuickEditService().SetRating(ids, rating))
        {
            Dispatcher.UIThread.Post(LoadFromDatabase);
        }
    }

    public bool IsSelectionStatusOpen => ActiveDropdown == "selection-status";

    public bool IsSelectionRateOpen => ActiveDropdown == "selection-rate";

    [RelayCommand]
    private void ToggleSelectionStatus() => ActiveDropdown = ActiveDropdown == "selection-status" ? null : "selection-status";

    [RelayCommand]
    private void ToggleSelectionRate() => ActiveDropdown = ActiveDropdown == "selection-rate" ? null : "selection-rate";

    /// <summary>Add books ▸ Manage folders…: Preferences › Libraries, where book folders live.</summary>
    [RelayCommand]
    private void ManageFolders()
    {
        ActiveDropdown = null;
        _goLibrarySettings();
    }
}
