namespace Paperbunkr.App.Models;

/// <summary>
/// One entry in Library's "Add to Reading List" flyout (docs/superpowers/specs/2026-08-24-library-
/// multiselect-slice2-design.md §2) - deliberately not <see cref="ReadingListSummary"/>, which
/// carries Reading screen sidebar-only required fields (<c>DeleteConfirm</c>, <c>HasTag</c>) that
/// have no meaning in a simple pick-a-list menu.
/// </summary>
public sealed class ReadingListOption
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}
