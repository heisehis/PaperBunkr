namespace Paperbunkr.Data.Entities;

/// <summary>
/// Library screen's display mode (docs/superpowers/specs/2026-08-09-library-toolbar-design.md
/// Phase A). Was <c>Grid</c>/<c>List</c> only (docs/superpowers/specs/
/// 2026-08-06-cover-thumbnails-design.md §5); extended to all 7 grid/card modes the toolbar's
/// Display dropdown offered. An 8th, <c>IssueList</c> ("Comic List"), was added then removed
/// (2026-09-03) - it was a redundant flat per-issue list that <see cref="Details"/> already
/// covers; the <c>IssueListScreenViewModel</c> it used lives on purely as the shared
/// sort/group engine feeding every mode's rows. A data migration remaps persisted
/// <c>'IssueList'</c> to <c>'Details'</c>.
///
/// UI rework Phase 4a (docs/superpowers/specs/2026-08-27-library-browsing-4a-poster-grid-
/// design.md) collapsed the near-duplicate <c>CompactGrid</c>/<c>ComfortableGrid</c>/
/// <c>CoverOnlyGrid</c> into a single <see cref="PosterGrid"/> - the continuous
/// <c>LibraryGridDensity</c> slider now carries the size distinction and
/// <c>LibraryShowTileTitles</c> carries the text-on/off one. <see cref="PosterGrid"/> is first so
/// it's the CLR default (0), the desired default, and the EF sentinel all at once. Stored via
/// <c>HasConversion&lt;string&gt;()</c>; a data migration remaps persisted legacy names.
/// </summary>
public enum LibraryViewMode
{
    PosterGrid,
    PanoramaGrid,
    List,
    Details,
    Tiles,
}
