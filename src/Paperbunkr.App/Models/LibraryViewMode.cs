namespace Paperbunkr.App.Models;

/// <summary>
/// Library screen's display mode (docs/superpowers/specs/2026-08-09-library-toolbar-design.md
/// Phase A). Was <c>Grid</c>/<c>List</c> only (docs/superpowers/specs/
/// 2026-08-06-cover-thumbnails-design.md §5); extended to all 7 modes the toolbar's Display
/// dropdown now offers.
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
}
