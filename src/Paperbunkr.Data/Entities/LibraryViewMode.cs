namespace Paperbunkr.Data.Entities;

/// <summary>
/// Library screen's display mode (docs/superpowers/specs/2026-08-09-library-toolbar-design.md
/// Phase A). Was <c>Grid</c>/<c>List</c> only (docs/superpowers/specs/
/// 2026-08-06-cover-thumbnails-design.md §5); extended to all 7 grid/card modes the toolbar's
/// Display dropdown offered, then an 8th, <see cref="IssueList"/>, folding in the former
/// standalone Comic List screen (docs/superpowers/specs/
/// 2026-08-18-issue-list-pluggable-sort-group-design.md) as a mode instead of a separate
/// rail-nav destination - matching CE's real model of one filtered query feeding several
/// <c>ItemViewMode</c>s rather than a parallel, unfiltered screen.
/// </summary>
public enum LibraryViewMode
{
    CompactGrid,
    ComfortableGrid,
    CoverOnlyGrid,
    PanoramaGrid,
    List,
    Details,
    Tiles,
    IssueList,
}
