namespace Paperbunkr.Data.Entities;

/// <summary>
/// Library screen's display mode (docs/superpowers/specs/2026-08-09-library-toolbar-design.md
/// Phase A). Was <c>Grid</c>/<c>List</c> only (docs/superpowers/specs/
/// 2026-08-06-cover-thumbnails-design.md §5); extended to all 7 grid/card modes the toolbar's
/// Display dropdown offered. An 8th, <c>IssueList</c> ("Comic List"), was added then removed
/// (2026-09-03) - it was a redundant flat per-issue list that <c>Details</c> already covered; the
/// <c>IssueListScreenViewModel</c> it used lives on purely as the shared sort/group engine feeding
/// every mode's rows.
///
/// UI rework Phase 4a (docs/superpowers/specs/2026-08-27-library-browsing-4a-poster-grid-
/// design.md) collapsed the near-duplicate <c>CompactGrid</c>/<c>ComfortableGrid</c>/
/// <c>CoverOnlyGrid</c> into a single <c>PosterGrid</c> - the continuous <c>LibraryGridDensity</c>
/// slider carried the size distinction and <c>LibraryShowTileTitles</c> the text-on/off one.
///
/// Master-Detail redesign (docs/superpowers/specs/2026-09-14-library-visual-redesign-design.md §2)
/// reduced this from 5 values (<c>PosterGrid</c>/<c>PanoramaGrid</c>/<c>List</c>/<c>Details</c>/
/// <c>Tiles</c>) to 3: <see cref="PosterGrid"/> absorbs Panorama/Tiles as the independent
/// <see cref="LibraryGridCoverFit"/> toggle (an earlier draft of this design tried to put
/// <c>Tiles</c> under <see cref="List"/> instead as a density toggle - wrong, since <c>Tiles</c> was
/// always the same wrap-grid rendering family as Poster/Panorama, not the genuinely different
/// <c>ListBox</c>-based container <see cref="List"/> actually is; corrected during implementation).
/// <see cref="List"/> is unchanged, no sub-toggle. <see cref="DetailsTable"/> is the old
/// <c>Details</c> renamed (unchanged column system).
///
/// <see cref="PosterGrid"/> deliberately **keeps its original name** rather than becoming a more
/// accurate "Grid" - it's still first/CLR-default (0)/EF sentinel, exactly as before, and every
/// historical migration checkpoint's physical column default is the raw string <c>"PosterGrid"</c>.
/// Renaming it was tried during implementation and reverted: any code path that lets EF fall back to
/// the column's DB default (e.g. `GetOrCreateAppSettings()` running against a database mid-migration,
/// which every "verify this migration in isolation" test in this project does) would try to parse
/// that stale stored string into the live enum and fail once the member name no longer matched
/// (`Cannot convert string value 'PosterGrid' ... to any value in the mapped 'LibraryViewMode' enum`).
/// Fixing that the "obvious" way - an `AlterColumn` updating the physical default to match the new
/// name - reintroduces a *worse*, already-independently-documented problem: any `AlterColumn` on
/// `AppSettings` forces SQLite's full-table-rebuild strategy, which silently drops
/// `LibraryGroupField`/`LibrarySortField`/`LibrarySortDirection` (unmapped since
/// `UnifyLibrarySortGroupFields`, 2026-09-03 - see `project_paperbunkr_migration_rollback_orphan_
/// column_bug` memory) - confirmed by reproducing the drop against a real seeded scratch database
/// while chasing this exact rename. Keeping the identifier `PosterGrid` needs zero schema change at
/// all for the default value, sidestepping both problems at once; view-mode-derived C# property
/// names elsewhere (`LibraryScreenViewModel.IsGridView` etc.) are free to use clearer language than
/// the enum member itself.
///
/// Stored via <c>HasConversion&lt;string&gt;()</c>; the <c>ConsolidateLibraryViewModes</c> migration
/// remaps the other persisted legacy names (<c>PanoramaGrid</c>/<c>Tiles</c>/<c>Details</c>).
/// </summary>
public enum LibraryViewMode
{
    PosterGrid,
    List,
    DetailsTable,
}
