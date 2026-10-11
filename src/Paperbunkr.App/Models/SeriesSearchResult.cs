using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// One candidate series in a search-results list (MediaRelation creation; Continuity "add series").
/// Implements <see cref="ISelectableCard"/> for multi-select
/// (docs/superpowers/specs/2026-08-28-bulk-selection-lists-continuities-events-design.md).
/// </summary>
public sealed partial class SeriesSearchResult : ObservableObject, ISelectableCard
{
    public required int SeriesId { get; init; }
    public required string Name { get; init; }

    /// <summary>"Also in: X, Y" when the series already belongs to other continuities (docs/superpowers/specs/2026-10-06-smart-features-design.md §7.1); null otherwise.</summary>
    public string? OtherContinuities { get; init; }

    public bool HasOtherContinuities => !string.IsNullOrEmpty(OtherContinuities);

    public int Id => SeriesId;

    [ObservableProperty]
    private bool _isSelected;
}
