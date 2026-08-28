using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row in an "Add issue" search-results list (Reading Lists / Story Events). Implements
/// <see cref="ISelectableCard"/> so <see cref="Paperbunkr.App.Services.TileSelectionController{TCard}"/>
/// can drive multi-select (docs/superpowers/specs/2026-08-28-bulk-selection-lists-continuities-events-design.md).
/// </summary>
public partial class IssueSearchResult : ObservableObject, ISelectableCard
{
    public int IssueId { get; init; }

    /// <summary>Owning series - drives "add all of this series".</summary>
    public int SeriesId { get; init; }

    public string DisplayLabel { get; init; } = string.Empty;

    public int Id => IssueId;

    [ObservableProperty]
    private bool _isSelected;
}
