namespace Paperbunkr.App.Models;

/// <summary>
/// Minimal shape <see cref="Paperbunkr.App.Services.TileSelectionController{TCard}"/> needs from a
/// tile/card model - implemented by both <see cref="IssueCardSample"/> (Detail's issue grid) and
/// <see cref="IssueListRow"/> (Library's issue-granularity grids/list), so one controller can drive
/// selection for either without depending on either concretely. See docs/superpowers/specs/
/// 2026-08-24-library-multiselect-slice1-design.md §2.
/// </summary>
public interface ISelectableCard
{
    int Id { get; }
    bool IsSelected { get; set; }
}
